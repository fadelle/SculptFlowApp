using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Services.Ai;
using Omni.App.Business.Services.BusinessTypes;
using Omni.App.Business.Services.Contacts;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.BusinessTypes;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;
using Omni.App.Tests.Helpers;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.Repositories;

/// <summary>The contact's language through the real services and schema.sql (plan §3.7): the database function gives the same
/// code as ContactLanguage, the check constraint keeps free text out, and a contact who writes English and then Arabizi
/// reaches n8n with contactLanguage "en" on the first message and "ar" on the second, after the AI's update_contact.</summary>
public class ContactLanguageWalkTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly CacheManager _cache = new(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance);
    private readonly Mock<IMessageTracer> _tracer = new();
    private readonly FakeHttpHandler _http = new();

    private ContactService Contacts(ApplicationDbContext db) =>
        new(new ContactRepository(db), new UnitOfWork(db), Mock.Of<IEventLogger>(), Mock.Of<ICatalogService>(), new ClientRepository(db, _cache),
            new BusinessTypeService(new BusinessTypeRepository(db, _cache), new ClientRepository(db, _cache), new ContactRepository(db),
                new UnitOfWork(db), TextKit.Context(), TimeProvider.System),
            _tracer.Object);

    private AiTriggerNotifier Notifier(ApplicationDbContext db)
    {
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
        var types = new BusinessTypeRepository(db, _cache);
        var profiles = new AiProfileService(new ClientRepository(db, _cache), types,
            new Terminology(TextKit.Catalog, types, Mock.Of<ICurrentClientContext>()), new ContactRepository(db));
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:AiWebhookUrl"] = "https://n8n.example/ai" }).Build();
        return new AiTriggerNotifier(_http.CreateClient(), settings, NullLogger<AiTriggerNotifier>.Instance, entitlements.Object, profiles,
            BotKit.Inbox().Object, BotKit.Runtime().Object, _tracer.Object);
    }

    [PostgresFact]
    public async Task The_database_function_gives_the_same_code_as_ContactLanguage()
    {
        var inputs = ContactLanguageTests.Cases.Select(row => (string?)row[0])
            .Concat(ContactLanguage.Names.Keys).Concat(ContactLanguage.Codes).ToList();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var raw in inputs)
        {
            await using var command = new NpgsqlCommand("select crm.normalize_language(@raw)", connection);
            command.Parameters.Add(new NpgsqlParameter("raw", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)raw ?? DBNull.Value });
            var sql = await command.ExecuteScalarAsync();
            Assert.True(Equals(ContactLanguage.Normalize(raw), sql is DBNull ? null : (string?)sql), $"'{raw}': C# {ContactLanguage.Normalize(raw) ?? "null"}, SQL {sql}");
        }
    }

    [PostgresFact]
    public async Task The_column_only_takes_a_two_letter_code()
    {
        var client = MakeClient();
        var contact = MakeContact(client.Id);
        contact.PreferredLanguage = "ar";
        await SeedAsync(client, contact);

        await using var db = NewContext();
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"update crm.contacts set preferred_language = {"Arabic"} where id = {contact.Id}"));
        Assert.Contains("ck_contacts_preferred_language", error.Message);
    }

    [PostgresFact]
    public async Task A_contact_walks_from_english_to_arabic_over_two_messages()
    {
        var client = MakeClient(businessType: TextKit.Clinic);
        client.DefaultLanguage = AppLanguage.French;
        await SeedAsync(client);

        Guid contactId;
        await using (var db = NewContext())
        {
            // Created in the dashboard with a language name: stored as its code.
            contactId = (await Contacts(db).CreateOrGetAsync(new CreateContactRequest(client.Id, null, "Karim", null, null, "961700001", null,
                "whatsapp", null, null, null, "English", null, null, null))).Contact.Id;
        }

        // Message 1 (English): the trigger carries the stored language next to the client's default.
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await using (var db = NewContext()) await Notifier(db).NotifyAsync(Payload(client.Id, contactId, "Hi, can I book?"));
        Assert.Equal(("en", "fr"), Languages(_http.Requests[^1].Body));

        // The contact switches to Arabizi; the AI's update_contact names it in words.
        await using (var db = NewContext())
        {
            await Contacts(db).UpdateContextAsync(client.Id, contactId, new UpdateContactContextRequest(null, "Arabizi", null, null, null, null, null));
        }

        // Message 2: the next trigger reads the new code.
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await using (var db = NewContext()) await Notifier(db).NotifyAsync(Payload(client.Id, contactId, "feye e7jez bukra?"));
        Assert.Equal(("ar", "fr"), Languages(_http.Requests[^1].Body));

        // A guess that is no language keeps Arabic and leaves a trace step.
        await using (var db = NewContext())
        {
            await Contacts(db).UpdateContextAsync(client.Id, contactId, new UpdateContactContextRequest(null, "emoji", null, null, null, null, null));
        }
        await using (var db = NewContext())
        {
            Assert.Equal("ar", (await db.Contacts.AsNoTracking().SingleAsync(c => c.Id == contactId)).PreferredLanguage);
        }
        _tracer.Verify(t => t.Step(TraceSteps.ContactLanguageIgnored, TraceOutcomes.Skipped, It.Is<TraceIds>(i => i.ClientId == client.Id),
            null, It.IsAny<object?>()), Times.Once);

        // Without a contact (a trigger that has none) the field is simply null.
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await using (var db = NewContext()) await Notifier(db).NotifyAsync(Payload(client.Id, null, "?"));
        Assert.Equal((null, "fr"), Languages(_http.Requests[^1].Body));
    }

    private static AiTriggerPayload Payload(Guid clientId, Guid? contactId, string text) =>
        new(clientId, Guid.NewGuid(), contactId, Guid.NewGuid(), "whatsapp", "text", text);

    private static (string? Contact, string? Client) Languages(string? body)
    {
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        return (root.GetProperty("contactLanguage").GetString(), root.GetProperty("language").GetString());
    }
}
