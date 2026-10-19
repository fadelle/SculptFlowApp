using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Mappers.BusinessTypes;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Services.BusinessTypes;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Helpers;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.BusinessTypes;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;

namespace Omni.App.Tests.Repositories;

/// <summary>Business types and pipeline stages against the throwaway Postgres: the two built-in types schema.sql seeds,
/// the cached list, the counts, editing a type, and moving a client (and its contacts) to another type.</summary>
public class BusinessTypeRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly CacheManager _cache = new(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance);
    private readonly PostgresFixture _fixture = fixture;

    private ApplicationDbContext CachedContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(new CacheInvalidationInterceptor(_cache)).Options);

    private BusinessTypeService Service(ApplicationDbContext db) => new(new BusinessTypeRepository(db, _cache), new ClientRepository(db, _cache),
        new ContactRepository(db), new UnitOfWork(db), TextKit.Context(), TimeProvider.System);

    private static string NewCode() => "t_" + Guid.NewGuid().ToString("N")[..10];

    private static SaveBusinessTypeRequest Request(string code, params (string Key, string Category)[] extra) => new(
        code,
        new Dictionary<string, string> { ["en"] = "Test " + code },
        new Dictionary<string, Dictionary<string, TermForms>> { ["en"] = TermName.All.ToDictionary(t => t, t => new TermForms(t, t + "s")) },
        "",
        99,
        StageCategory.All.Select(c => new SaveStageRequest(c + "_stage", c, new Dictionary<string, string> { ["en"] = c }))
            .Concat(extra.Select(e => new SaveStageRequest(e.Key, e.Category, new Dictionary<string, string> { ["en"] = e.Key })))
            .ToList());

    [PostgresFact]
    public async Task Schema_seeds_the_two_built_in_types_with_every_category_covered()
    {
        await using var db = CachedContext();
        var types = await new BusinessTypeRepository(db).ListReadOnlyAsync();
        var firstType = Assert.Single(types, t => t.Code == TextKit.Clinic);
        var general = Assert.Single(types, t => t.Code == TextKit.General);

        Assert.True(firstType.IsActive && general.IsActive);
        Assert.True(firstType.SortOrder < general.SortOrder);
        Assert.Equal(["new", "contacted", "qualified"], firstType.Stages.Take(3).Select(s => s.Key));
        Assert.Equal("medical_question", firstType.Stages[^1].Key);
        Assert.Equal(["new", "contacted", "qualified", "booked", "attended", "no_show", "customer", "not_interested", "lost", "needs_human"],
            general.Stages.Select(s => s.Key));
        foreach (var type in new[] { firstType, general })
        {
            Assert.All(StageCategory.All, c => Assert.Contains(type.Stages, s => s.Category == c));
            Assert.Equal(type.Stages.OrderBy(s => s.SortOrder).Select(s => s.Key), type.Stages.Select(s => s.Key));
            var terms = BusinessTypeMapper.ParseTerminology(type.TerminologyJson)["en"];
            Assert.All(TermName.All, t => Assert.False(string.IsNullOrWhiteSpace(terms[t].One + terms[t].Other)));
            // The AI profile (the business-specific part of the n8n assistant's prompt) is filled, not the placeholder.
            Assert.DoesNotContain("Placeholder", type.AiProfile);
            Assert.Contains("search_knowledge", type.AiProfile);
        }
        Assert.Contains("action=handoff", firstType.AiProfile);
        Assert.Equal("Customer", BusinessTypeMapper.LabelOf(general.Stages.Single(s => s.Key == "customer"), "fr"));
    }

    [PostgresFact]
    public async Task A_client_must_name_an_existing_business_type()
    {
        var client = MakeClient();
        client.BusinessTypeCode = "no_such_type";
        await Assert.ThrowsAsync<DbUpdateException>(() => SeedAsync(client));
    }

    [PostgresFact]
    public async Task The_cached_list_is_served_until_a_type_or_stage_is_saved()
    {
        var code = NewCode();
        await using (var db = CachedContext()) await Service(db).CreateAsync(Request(code));

        await using (var db = CachedContext())
        {
            Assert.Equal(7, (await new BusinessTypeRepository(db, _cache).GetReadOnlyAsync(code))!.Stages.Count);
            await db.Database.ExecuteSqlInterpolatedAsync($"delete from crm.pipeline_stages where business_type_code = {code} and key = 'open_stage'");
        }
        await using (var db = CachedContext())
        {
            Assert.Equal(7, (await new BusinessTypeRepository(db, _cache).GetReadOnlyAsync(code))!.Stages.Count);   // still the cached copy
        }

        await using (var db = CachedContext())
        {
            await Service(db).UpdateAsync(code, Request(code, ("second_open", StageCategory.Open)));
        }
        await using var read = CachedContext();
        var reread = (await new BusinessTypeRepository(read, _cache).GetReadOnlyAsync(code))!;
        Assert.Equal(["open_stage", "booked_stage", "attended_stage", "no_show_stage", "converted_stage", "closed_lost_stage", "needs_human_stage", "second_open"],
            reread.Stages.Select(s => s.Key));
    }

    [PostgresFact]
    public async Task Editing_a_type_adds_updates_and_removes_stages_and_refuses_to_remove_one_in_use()
    {
        var code = NewCode();
        await using (var db = CachedContext()) await Service(db).CreateAsync(Request(code, ("extra", StageCategory.Open), ("busy", StageCategory.Booked)));
        var client = MakeClient();
        client.BusinessTypeCode = code;
        var contact = MakeContact(client.Id, stage: "busy");
        await SeedAsync(client, contact);

        await using (var db = CachedContext())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).UpdateAsync(code, Request(code)));
            Assert.Equal("These stages still have contacts in them, so they can't be removed: busy.", TextKit.Localizer().Localize(error));
        }
        await using (var db = CachedContext())
        {
            var updated = await Service(db).UpdateAsync(code, Request(code, ("busy", StageCategory.NeedsHuman)) with { AiProfile = "Be brief." });
            Assert.Equal("Be brief.", updated.AiProfile);
            Assert.DoesNotContain(updated.Stages, s => s.Key == "extra");
            Assert.Equal((StageCategory.NeedsHuman, 80), updated.Stages.Where(s => s.Key == "busy").Select(s => (s.Category, s.SortOrder)).Single());
        }
        await using (var db = CachedContext())
        {
            var repo = new BusinessTypeRepository(db);
            Assert.Equal(1, (await repo.CountClientsByTypeAsync())[code]);
            Assert.Equal(new Dictionary<string, int> { ["busy"] = 1 }, await repo.CountContactsByStageAsync(code));
            Assert.True(await repo.ExistsAsync(code));
            await Assert.ThrowsAsync<DuplicateRecordException>(() => Service(db).CreateAsync(Request(code)));
        }
    }

    [PostgresFact]
    public async Task Switching_a_type_off_keeps_it_out_of_sign_up_but_not_out_of_the_admin_list()
    {
        var code = NewCode();
        await using (var db = CachedContext()) await Service(db).CreateAsync(Request(code));
        await using (var db = CachedContext()) Assert.False((await Service(db).SetActiveAsync(code, false)).IsActive);

        await using var read = CachedContext();
        Assert.DoesNotContain(await Service(read).ListOptionsAsync(), o => o.Code == code);
        Assert.Contains(await Service(read).ListAsync(), t => t.Code == code && !t.IsActive);
        Assert.False(await Service(read).IsActiveAsync(code));
    }

    [PostgresFact]
    public async Task Moving_a_client_to_another_type_moves_every_contact_to_the_matching_stage()
    {
        var client = MakeClient(businessType: TextKit.Clinic);
        var contacts = new[]
        {
            MakeContact(client.Id, stage: "contacted"), MakeContact(client.Id, stage: "consultation_booked"), MakeContact(client.Id, stage: "consultation_booked"), // leftover-ok: firstType stage keys
            MakeContact(client.Id, stage: "surgery_booked"), MakeContact(client.Id, stage: "medical_question"), MakeContact(client.Id, stage: "not_interested"), // leftover-ok: firstType stage keys
        };
        var other = MakeClient(businessType: TextKit.Clinic);
        var untouched = MakeContact(other.Id, stage: "consultation_booked"); // leftover-ok: firstType stage key
        await SeedAsync([client, other, .. contacts, untouched]);

        await using (var db = CachedContext())
        {
            var preview = await Service(db).PreviewClientChangeAsync(client.Id, TextKit.General);
            Assert.Equal((TextKit.Clinic, 6), (preview.FromCode, preview.ContactCount));
            Assert.Contains(preview.Moves, m => m is { FromKey: "consultation_booked", Count: 2, ToKey: "booked", ToLabel: "Booked" }); // leftover-ok: firstType stage key
        }
        await using (var db = CachedContext())
        {
            await Service(db).ChangeClientTypeAsync(client.Id, TextKit.General);
        }

        await using var read = CachedContext();
        Assert.Equal(TextKit.General, (await read.Clients.SingleAsync(c => c.Id == client.Id)).BusinessTypeCode);
        Assert.Equal(new Dictionary<string, int> { ["contacted"] = 1, ["booked"] = 2, ["customer"] = 1, ["needs_human"] = 1, ["not_interested"] = 1 },
            await new ContactRepository(read).CountByStageAsync(client.Id));
        Assert.Equal("consultation_booked", (await read.Contacts.SingleAsync(l => l.Id == untouched.Id)).StageKey); // leftover-ok: firstType stage key
        Assert.Equal(["new", "contacted", "qualified", "booked"], (await Service(read).ListStagesForClientAsync(client.Id)).Take(4).Select(s => s.Key));
    }
}
