using Omni.App.Business.Managers;
using Omni.App.Business.Services.Contacts;

namespace Omni.App.Tests.Services;

/// <summary>Milestone C S3: stop/start words and the opt-out state (crm.contacts.marketing_opt_in / opted_out_at).</summary>
public class OptOutServiceTests
{
    private const string Stop = "stop,stopall,unsubscribe,arret,desabonner,desinscrire,توقف,ايقاف,الغاء الاشتراك";
    private const string Start = "start,unstop,subscribe,demarrer,اشتراك";

    [Theory]
    [InlineData("stop", true)]
    [InlineData("  STOP! ", true)]
    [InlineData("/stop", true)]
    [InlineData("Stop.", true)]
    [InlineData("Arrêt", true)]
    [InlineData("ARRET", true)]
    [InlineData("Désabonner", true)]
    [InlineData("توقف", true)]
    [InlineData("تَوَقَّف", true)]
    [InlineData("إيقاف", true)]
    [InlineData("الغاء  الاشتراك", true)]
    [InlineData("إلغاء الاشتراك؟", true)]
    [InlineData("start", false)]
    [InlineData("Démarrer", false)]
    [InlineData("اشتراك", false)]
    [InlineData("please stop calling me", null)]
    [InlineData("cancel", null)]
    [InlineData("stopp", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_a_whole_message_stop_or_start_word_matches(string? text, bool? expected) =>
        Assert.Equal(expected, OptOutKeywords.Match(text, Stop, Start));

    [Fact]
    public void Long_messages_are_never_keywords() =>
        Assert.Null(OptOutKeywords.Match("stop " + new string(' ', 50), "stop", "start"));

    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public OptOutServiceTests()
    {
        _config.SetupGet(c => c.OptOutEnabled).Returns(true);
        _config.SetupGet(c => c.OptOutSendConfirmation).Returns(true);
        _config.SetupGet(c => c.OptOutStopKeywords).Returns(Stop);
        _config.SetupGet(c => c.OptOutStartKeywords).Returns(Start);
        _config.SetupGet(c => c.LocalizationLanguages).Returns("en,ar,fr");
    }

    private OptOutService Sut()
    {
        var terminology = new Mock<ITerminology>();
        terminology.Setup(t => t.Defaults(It.IsAny<string>())).Returns(TextKit.DefaultTerms());
        terminology.Setup(t => t.ForBusinessTypeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TextKit.DefaultTerms());
        return new OptOutService(_config.Object, _contacts.Object, _clients.Object, new ReaderTextFactory(TextKit.Catalog, terminology.Object, _config.Object),
            _events.Object, _uow.Object, TimeProvider.System);
    }

    private Contact NewContact(string? language = null) => new() { Id = Guid.NewGuid(), ClientId = _clientId, MarketingOptIn = true, PreferredLanguage = language };

    [Fact]
    public void The_switch_turns_keyword_matching_off()
    {
        Assert.True(Sut().MatchKeyword("STOP"));
        _config.SetupGet(c => c.OptOutEnabled).Returns(false);
        Assert.Null(Sut().MatchKeyword("STOP"));
    }

    [Fact]
    public void Applying_sets_both_fields_and_logs_only_real_changes()
    {
        var contact = NewContact();
        var conversationId = Guid.NewGuid();
        Assert.True(Sut().Apply(contact, true, MessageOrigin.WhatsAppCustomer, conversationId));
        Assert.False(contact.MarketingOptIn);
        Assert.NotNull(contact.OptedOutAt);
        Assert.False(Sut().Apply(contact, true, MessageOrigin.WhatsAppCustomer));
        _events.Verify(e => e.Log(_clientId, EventTypes.ContactOptedOut, contact.Id, conversationId, null, MessageOrigin.WhatsAppCustomer, "{}"), Times.Once);

        Assert.True(Sut().Apply(contact, false, MessageOrigin.Dashboard));
        Assert.True(contact.MarketingOptIn);
        Assert.Null(contact.OptedOutAt);
        _events.Verify(e => e.Log(_clientId, EventTypes.ContactOptedIn, contact.Id, null, null, MessageOrigin.Dashboard, "{}"), Times.Once);
    }

    [Fact]
    public async Task The_confirmation_names_the_business_in_the_contacts_language_then_the_client_default()
    {
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { Id = _clientId, Name = "Glow", DefaultLanguage = "fr", BusinessTypeCode = TextKit.General });

        Assert.Equal("تم إلغاء اشتراكك: لن تصلك رسائل من Glow هنا بعد الآن. أرسل START أو «اشتراك» لإعادة الاشتراك.",
            await Sut().ConfirmationTextAsync(_clientId, NewContact("ar"), true));
        Assert.Equal("Vous êtes de nouveau abonné aux messages de Glow.", await Sut().ConfirmationTextAsync(_clientId, NewContact("es"), false));

        _config.SetupGet(c => c.OptOutSendConfirmation).Returns(false);
        Assert.Null(await Sut().ConfirmationTextAsync(_clientId, NewContact(), true));
    }

    [Fact]
    public async Task A_member_switch_saves_only_a_change_and_only_for_the_clients_contact()
    {
        var contact = NewContact();
        _contacts.Setup(c => c.GetAsync(_clientId, contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);

        Assert.Null(await Sut().SetAsync(_clientId, Guid.NewGuid(), true));
        var response = await Sut().SetAsync(_clientId, contact.Id, true);
        Assert.Equal((false, true), (response!.MarketingOptIn, response.OptedOutAt is not null));
        await Sut().SetAsync(_clientId, contact.Id, true);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
