using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Business.Services.Inbox;

namespace Omni.App.Tests.Services;

public class InboxSettingsServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IInboxSettingsRepository> _settings = new();
    private readonly Mock<IBotRepository> _bots = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IChannelIntegrationRepository> _channels = new();
    private readonly ChannelIntegration _whatsApp;
    private readonly ChannelIntegration _telegram;
    private readonly List<InboxSettings> _rows = [];
    private readonly List<Bot> _botList = [];

    public InboxSettingsServiceTests()
    {
        _clients.Setup(r => r.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Glow" });
        var now = DateTimeOffset.UtcNow;
        _whatsApp = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ConversationChannel.WhatsApp, Status = ChannelIntegrationStatus.Connected, DisplayName = "+961 70", CreatedAt = now };
        _telegram = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ConversationChannel.Telegram, Status = ChannelIntegrationStatus.Connected, CreatedAt = now.AddMinutes(1) };
        _channels.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => [_whatsApp, _telegram]);
        _settings.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _rows.ToList());
        _settings.Setup(r => r.GetForAccountAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid account, CancellationToken _) => _rows.FirstOrDefault(r => r.ChannelIntegrationId == account));
        _settings.Setup(r => r.Add(It.IsAny<InboxSettings>())).Callback<InboxSettings>(_rows.Add);
        _bots.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _botList.ToList());
        Plan(aiAssistant: true);
    }

    private InboxSettingsService Sut() => new(_settings.Object, _bots.Object, _clients.Object, _entitlements.Object, _channels.Object,
        [Mock.Of<IChannelSender>(s => s.Channel == ConversationChannel.WhatsApp), Mock.Of<IChannelSender>(s => s.Channel == ConversationChannel.Telegram)], _uow.Object);

    private void Plan(bool aiAssistant, int? maxBots = null)
    {
        var values = new Dictionary<string, string> { [EntitlementKeys.AiAssistant] = aiAssistant ? "true" : "false" };
        values[EntitlementKeys.MaxAiBots] = maxBots?.ToString() ?? EntitlementCatalog.Unlimited;
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientEntitlements(true, true, SubscriptionStatus.Active, "ai-pro", "AI Pro", null, values));
    }

    private Bot AddBot(string status = BotStatus.Active, int minutesOld = 10)
    {
        var bot = new Bot { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Bot " + _botList.Count, Template = BotTemplate.Support, Status = status, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesOld) };
        _botList.Add(bot);
        return bot;
    }

    private InboxSettings Assign(ChannelIntegration account, Bot? bot, bool aiEnabled = true, bool newOn = true, bool reopenedOn = true)
    {
        var row = new InboxSettings { Id = Guid.NewGuid(), ClientId = _clientId, ChannelIntegrationId = account.Id, AiEnabled = aiEnabled, BotId = bot?.Id,
            AiBotNewConversations = newOn, AiBotReopenedConversations = reopenedOn };
        _rows.Add(row);
        return row;
    }

    [Fact]
    public async Task An_account_without_settings_is_answered_by_the_team()
    {
        var settings = await Sut().GetAsync(_clientId);
        Assert.Equal([_whatsApp.Id, _telegram.Id], settings.Accounts.Select(a => a.ChannelIntegrationId));
        Assert.All(settings.Accounts, a => Assert.Equal((false, null, true, true, AiBotBlockReason.AiDisabled),
            (a.AiEnabled, a.BotId, a.AiBotNewConversations, a.AiBotReopenedConversations, a.BlockReason)));
        Assert.Equal(ConversationMode.Human, await Sut().InitialModeAsync(_clientId, ConversationChannel.WhatsApp));
    }

    [Fact]
    public async Task Each_account_answers_with_its_own_bot_and_switches_and_one_bot_can_serve_several()
    {
        var bot = AddBot();
        Assign(_whatsApp, bot, newOn: true, reopenedOn: false);
        Assign(_telegram, bot, newOn: false, reopenedOn: true);

        var wa = await Sut().ResolveAsync(_clientId, ConversationChannel.WhatsApp);
        var tg = await Sut().ResolveAsync(_clientId, ConversationChannel.Telegram);
        Assert.Equal((true, true, false, bot.Id), (wa.BotAnswers, wa.AnswersNew, wa.AnswersReopened, wa.BotId!.Value));
        Assert.Equal((true, false, true), (tg.BotAnswers, tg.AnswersNew, tg.AnswersReopened));
        Assert.Equal(ConversationMode.Ai, await Sut().InitialModeAsync(_clientId, ConversationChannel.WhatsApp));
        Assert.Equal(ConversationMode.Human, await Sut().InitialModeAsync(_clientId, ConversationChannel.Telegram));

        var page = await Sut().GetAsync(_clientId);
        Assert.All(page.Accounts, a => Assert.Equal((bot.Name, BotStatus.Active, (string?)null), (a.BotName, a.BotStatus, a.BlockReason)));
    }

    [Theory]
    [InlineData("ai_off")]
    [InlineData("no_bot")]
    [InlineData("draft")]
    [InlineData("inactive")]
    public async Task Ai_off_no_bot_or_a_bot_that_isnt_active_sends_the_account_to_the_team(string state)
    {
        var bot = AddBot(state switch { "draft" => BotStatus.Draft, "inactive" => BotStatus.Inactive, _ => BotStatus.Active });
        Assign(_whatsApp, state == "no_bot" ? null : bot, aiEnabled: state != "ai_off");
        Assign(_telegram, AddBot()); // another account, untouched

        var decision = await Sut().ResolveAsync(_clientId, ConversationChannel.WhatsApp);
        Assert.False(decision.BotAnswers);
        Assert.Equal(state switch { "ai_off" => AiBotBlockReason.AiDisabled, "no_bot" => AiBotBlockReason.NoBot, _ => AiBotBlockReason.BotInactive },
            decision.BlockReason);
        Assert.True((await Sut().ResolveAsync(_clientId, ConversationChannel.Telegram)).BotAnswers);
    }

    [Fact]
    public async Task The_plan_decides_first_and_only_the_oldest_active_bots_within_its_limit_answer()
    {
        var older = AddBot(minutesOld: 20);
        var newer = AddBot(minutesOld: 5);
        Assign(_whatsApp, older);
        Assign(_telegram, newer);

        Plan(aiAssistant: true, maxBots: 1);
        Assert.True((await Sut().ResolveAsync(_clientId, ConversationChannel.WhatsApp)).BotAnswers);
        Assert.Equal(AiBotBlockReason.BotLimit, (await Sut().ResolveAsync(_clientId, ConversationChannel.Telegram)).BlockReason);
        Assert.Equal(1, (await Sut().GetAsync(_clientId)).ActiveBotLimit);

        Plan(aiAssistant: false);
        Assert.Equal(AiBotBlockReason.PlanWithoutAi, (await Sut().ResolveAsync(_clientId, ConversationChannel.WhatsApp)).BlockReason);
        Assert.False((await Sut().GetAsync(_clientId)).PlanIncludesAiBot);
    }

    [Fact]
    public async Task A_channel_without_a_connected_account_has_no_bot()
    {
        _whatsApp.Status = ChannelIntegrationStatus.Disconnected;
        Assert.Equal(AiBotBlockReason.NoAccount, (await Sut().ResolveAsync(_clientId, ConversationChannel.WhatsApp)).BlockReason);
        Assert.Equal(AiBotBlockReason.NoAccount, (await Sut().ResolveAsync(_clientId, ConversationChannel.Facebook)).BlockReason);
        Assert.Single((await Sut().GetAsync(_clientId)).Accounts);
    }

    [Fact]
    public async Task A_connection_omni_cant_reply_on_has_no_ai_settings()
    {
        var instagram = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ConversationChannel.Instagram, Status = ChannelIntegrationStatus.Connected };
        _channels.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => [_whatsApp, _telegram, instagram]);
        Assert.DoesNotContain((await Sut().GetAsync(_clientId)).Accounts, a => a.ChannelIntegrationId == instagram.Id);
        Assert.Equal(AiBotBlockReason.NoAccount, (await Sut().ResolveAsync(_clientId, ConversationChannel.Instagram)).BlockReason);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAccountAsync(_clientId, instagram.Id, new UpdateAccountAiRequest(false, null, true, true)));
    }

    [Fact]
    public async Task Billing_off_lets_every_active_bot_answer()
    {
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
        Assign(_whatsApp, AddBot());
        Assign(_telegram, AddBot());
        Assert.True((await Sut().ResolveAsync(_clientId, ConversationChannel.WhatsApp)).BotAnswers);
        Assert.True((await Sut().ResolveAsync(_clientId, ConversationChannel.Telegram)).BotAnswers);
    }

    [Fact]
    public async Task The_first_save_creates_the_accounts_row_and_later_saves_change_only_that_account()
    {
        var bot = AddBot();
        Assign(_telegram, bot);

        await Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(true, bot.Id, false, true));
        var row = Assert.Single(_rows, r => r.ChannelIntegrationId == _whatsApp.Id);
        Assert.Equal((true, (Guid?)bot.Id, false, true), (row.AiEnabled, row.BotId, row.AiBotNewConversations, row.AiBotReopenedConversations));

        await Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(false, bot.Id, true, true));
        Assert.Equal(2, _rows.Count);
        Assert.False(row.AiEnabled);
        Assert.True(_rows.Single(r => r.ChannelIntegrationId == _telegram.Id).AiEnabled); // another account keeps AI on
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Turning_ai_on_needs_the_plan_and_an_active_bot_of_the_client()
    {
        var draft = AddBot(BotStatus.Draft);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(true, null, true, true)));
        var notActive = await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(true, draft.Id, true, true)));
        Assert.Equal("inboxSettings.errors.botNotActive", notActive.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(false, Guid.NewGuid(), true, true)));

        // AI off with a draft bot is fine: it is ready for when the bot is activated.
        await Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(false, draft.Id, true, true));
        Assert.Equal(draft.Id, Assert.Single(_rows).BotId);

        _entitlements.Setup(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.AiAssistant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.AiAssistant, EntitlementDeniedException.NotIncludedKey));
        var active = AddBot();
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().UpdateAccountAsync(_clientId, _whatsApp.Id, new UpdateAccountAiRequest(true, active.Id, true, true)));
    }

    [Fact]
    public async Task Unknown_clients_and_accounts_are_not_found()
    {
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync(other));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAccountAsync(other, _whatsApp.Id, new UpdateAccountAiRequest(false, null, true, true)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAccountAsync(_clientId, Guid.NewGuid(), new UpdateAccountAiRequest(false, null, true, true)));
        _telegram.Status = ChannelIntegrationStatus.Disconnected;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAccountAsync(_clientId, _telegram.Id, new UpdateAccountAiRequest(false, null, true, true)));
        _settings.Verify(r => r.Add(It.IsAny<InboxSettings>()), Times.Never);
    }
}
