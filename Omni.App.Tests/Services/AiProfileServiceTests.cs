using Omni.App.Business.Managers;
using Omni.App.Business.Services.Ai;

namespace Omni.App.Tests.Services;

/// <summary>The client facts the AI trigger carries to n8n: the client's own name, language and timezone, its business
/// type's AI profile and the type's words in the client's default language.</summary>
public class AiProfileServiceTests
{
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IBusinessTypeRepository> _types = new();
    private readonly Mock<ICurrentClientContext> _current = new();
    private readonly Mock<IContactRepository> _contacts = new();

    private AiProfileService Sut() => new(_clients.Object, _types.Object, new Terminology(TextKit.Catalog, _types.Object, _current.Object), _contacts.Object);

    private Client AddClient(string type, string language = AppLanguage.English)
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "Glow Studio", BusinessTypeCode = type, DefaultLanguage = language, Timezone = "Europe/Paris" };
        _clients.Setup(c => c.GetReadOnlyAsync(client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        return client;
    }

    [Fact]
    public async Task The_profile_carries_the_clients_facts_the_types_ai_profile_and_its_words()
    {
        var client = AddClient("salon");
        _types.Setup(t => t.GetReadOnlyAsync("salon", It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessType
        {
            Code = "salon",
            AiProfile = "You are the assistant of a hair salon.",
            TerminologyJson = """{"en": {"contact": {"one": "guest", "other": "guests"}, "booking": {"one": "visit", "other": "visits"}}}"""
        });

        var profile = await Sut().GetForClientAsync(client.Id);

        Assert.NotNull(profile);
        Assert.Equal(("Glow Studio", "salon", "en", "Europe/Paris"), (profile.ClientName, profile.BusinessType, profile.Language, profile.Timezone));
        Assert.Equal("You are the assistant of a hair salon.", profile.AiProfile);
        Assert.Equal(new TermForms("guest", "guests"), profile.Terms[TermName.Contact]);
        Assert.Equal(new TermForms("visit", "visits"), profile.Terms[TermName.Booking]);
        // Every term is present: the ones the type doesn't rename fall back to the default words.
        Assert.Equal(TermName.All.Order(), profile.Terms.Keys.Order());
        Assert.Equal(new TermForms("service", "services"), profile.Terms[TermName.Service]);
    }

    [Fact]
    public async Task The_words_follow_the_clients_default_language()
    {
        var client = AddClient("salon", AppLanguage.French);
        _types.Setup(t => t.GetReadOnlyAsync("salon", It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessType
        {
            Code = "salon",
            TerminologyJson = """{"en": {"contact": {"one": "guest", "other": "guests"}}, "fr": {"contact": {"one": "invité", "other": "invités"}}}"""
        });

        var profile = await Sut().GetForClientAsync(client.Id);

        Assert.Equal("fr", profile!.Language);
        Assert.Equal(new TermForms("invité", "invités"), profile.Terms[TermName.Contact]);
    }

    [Fact]
    public async Task A_missing_type_gives_an_empty_profile_with_default_words_and_a_missing_client_gives_null()
    {
        var client = AddClient("gone");

        var profile = await Sut().GetForClientAsync(client.Id);

        Assert.Equal("", profile!.AiProfile);
        Assert.Equal(new TermForms("contact", "contacts"), profile.Terms[TermName.Contact]);
        Assert.Null(await Sut().GetForClientAsync(Guid.NewGuid()));
    }
}
