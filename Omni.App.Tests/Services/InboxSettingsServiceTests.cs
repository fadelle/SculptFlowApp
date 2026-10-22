using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Business.Services.Inbox;

namespace Omni.App.Tests.Services;

public class InboxSettingsServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IInboxSettingsRepository> _settings = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IChannelIntegrationRepository> _channels = new();

    public InboxSettingsServiceTests()
    {
        _clients.Setup(r => r.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Glow" });
        Plan(aiAssistant: true);
    }

    private InboxSettingsService Sut() => new(_settings.Object, _clients.Object, _entitlements.Object, _channels.Object, _uow.Object);

    private InboxSettings Row(bool newOn, bool reopenedOn)
    {
        var row = new InboxSettings { Id = Guid.NewGuid(), ClientId = _clientId, AiBotNewConversations = newOn, AiBotReopenedConversations = reopenedOn };
        _settings.Setup(r => r.GetForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _settings.Setup(r => r.GetForClientAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        return row;
    }

    private void Plan(bool aiAssistant) =>
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientEntitlements(true, true,
            SubscriptionStatus.Active, "basic", "Basic", null,
            new Dictionary<string, string> { [EntitlementKeys.AiAssistant] = aiAssistant ? "true" : "false" }));

    [Fact]
    public async Task A_client_without_a_row_gets_the_defaults_with_the_bot_on()
    {
        Assert.Equal(new InboxSettingsDto(true, true, true), await Sut().GetAsync(_clientId));
        Assert.Equal(ConversationMode.Ai, await Sut().InitialModeAsync(_clientId));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(false, true, true, false, false)]   // a plan without the AI assistant overrides the client's switches
    public async Task The_plan_and_the_client_switches_decide_who_answers(bool plan, bool newOn, bool reopenedOn, bool botNew, bool botReopened)
    {
        Row(newOn, reopenedOn);
        Plan(plan);

        var settings = await Sut().GetAsync(_clientId);

        Assert.Equal((plan, newOn, reopenedOn, botNew, botReopened),
            (settings.PlanIncludesAiBot, settings.AiBotNewConversations, settings.AiBotReopenedConversations, settings.AiBotAnswersNew, settings.AiBotAnswersReopened));
        Assert.Equal(botNew ? ConversationMode.Ai : ConversationMode.Human, await Sut().InitialModeAsync(_clientId));
    }

    [Fact]
    public async Task Billing_off_means_the_bot_answers()
    {
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
        Assert.True((await Sut().GetAsync(_clientId)).AiBotAnswersNew);
    }

    [Fact]
    public async Task An_unknown_client_is_not_found()
    {
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync(other));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAsync(other, new UpdateInboxSettingsRequest(true, true)));
        _settings.Verify(r => r.Add(It.IsAny<InboxSettings>()), Times.Never);
    }

    [Fact]
    public async Task The_first_save_creates_the_client_wide_row()
    {
        InboxSettings? added = null;
        _settings.Setup(r => r.Add(It.IsAny<InboxSettings>())).Callback<InboxSettings>(s => added = s);

        await Sut().UpdateAsync(_clientId, new UpdateInboxSettingsRequest(false, true));

        Assert.Equal((_clientId, (Guid?)null, false, true),
            (added!.ClientId, added.ChannelIntegrationId, added.AiBotNewConversations, added.AiBotReopenedConversations));
        Assert.NotEqual(Guid.Empty, added.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_later_save_changes_the_row_only_when_something_changed()
    {
        var row = Row(true, true);

        await Sut().UpdateAsync(_clientId, new UpdateInboxSettingsRequest(true, true));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await Sut().UpdateAsync(_clientId, new UpdateInboxSettingsRequest(true, false));
        Assert.Equal((true, false), (row.AiBotNewConversations, row.AiBotReopenedConversations));
        Assert.True(row.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        _settings.Verify(r => r.Add(It.IsAny<InboxSettings>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private void BotPlan(string? maxAiBots, params (string Channel, int MinutesAgo, string Status)[] channels)
    {
        var values = new Dictionary<string, string> { [EntitlementKeys.AiAssistant] = "true" };
        if (maxAiBots is not null) values[EntitlementKeys.MaxAiBots] = maxAiBots;
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientEntitlements(true, true,
            SubscriptionStatus.Active, "ai-pro", "AI Pro", null, values));
        _channels.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(channels
            .Select(c => new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = c.Channel, Status = c.Status,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-c.MinutesAgo) })
            .ToList());
    }

    [Fact]
    public async Task Connected_channels_beyond_the_ai_bot_limit_go_to_the_team_in_connection_order()
    {
        // WhatsApp connected first, Telegram second; a disconnected channel doesn't use a bot.
        BotPlan("1", (ConversationChannel.Telegram, 10, ChannelIntegrationStatus.Connected),
            (ConversationChannel.WhatsApp, 60, ChannelIntegrationStatus.Connected),
            (ConversationChannel.Facebook, 90, ChannelIntegrationStatus.Disconnected));

        var settings = await Sut().GetAsync(_clientId);

        Assert.Equal([ConversationChannel.Telegram], settings.ChannelsWithoutAiBot!.ToArray());
        Assert.Equal(1, settings.AiBotLimit);
        Assert.True(settings.AiBotAnswersOn(ConversationChannel.WhatsApp));
        Assert.False(settings.AiBotAnswersOn(ConversationChannel.Telegram));
        Assert.True(settings.AiBotAnswersOn(ConversationChannel.Facebook)); // not connected: no bot used yet
        Assert.Equal(ConversationMode.Ai, await Sut().InitialModeAsync(_clientId, ConversationChannel.WhatsApp));
        Assert.Equal(ConversationMode.Human, await Sut().InitialModeAsync(_clientId, ConversationChannel.Telegram));
    }

    [Theory]
    [InlineData(null)]        // a plan made before AI bots were counted: no limit
    [InlineData("unlimited")]
    [InlineData("4")]          // more bots than connected channels
    public async Task Without_a_reached_ai_bot_limit_the_bot_answers_on_every_channel(string? maxAiBots)
    {
        BotPlan(maxAiBots, (ConversationChannel.Telegram, 10, ChannelIntegrationStatus.Connected),
            (ConversationChannel.WhatsApp, 60, ChannelIntegrationStatus.Connected));

        var settings = await Sut().GetAsync(_clientId);

        Assert.Empty(settings.ChannelsWithoutAiBot ?? new HashSet<string>());
        Assert.Equal(ConversationMode.Ai, await Sut().InitialModeAsync(_clientId, ConversationChannel.Telegram));
        if (maxAiBots is null or "unlimited")
        {
            _channels.Verify(r => r.ListForClientReadOnlyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task A_plan_with_no_ai_bots_keeps_every_connected_channel_with_the_team()
    {
        BotPlan("0", (ConversationChannel.WhatsApp, 60, ChannelIntegrationStatus.Connected));
        var settings = await Sut().GetAsync(_clientId);
        Assert.False(settings.AiBotAnswersOn(ConversationChannel.WhatsApp));
        Assert.Equal(ConversationMode.Human, await Sut().InitialModeAsync(_clientId, ConversationChannel.WhatsApp));
    }
}
