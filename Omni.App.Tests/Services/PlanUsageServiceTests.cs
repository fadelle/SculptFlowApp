using Omni.App.Business.Services.Billing;
using static Omni.App.Tests.Pages.PageKit;

namespace Omni.App.Tests.Services;

/// <summary>Billing page → plan usage: limits counted the way they are enforced, and this period's messages and AI replies.</summary>
public class PlanUsageServiceTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IAccountTokenRepository> _tokens = new();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IBotRepository> _bots = new();

    private PlanUsageService Sut() =>
        new(_entitlements.Object, _clients.Object, _tokens.Object, _integrations.Object, _bots.Object, TimeProvider.System);

    private void Plan(Dictionary<string, string> values, bool hasAccess = true) =>
        _entitlements.Setup(e => e.GetAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientEntitlements(true, hasAccess, SubscriptionStatus.Active, "growth", "Growth", null, values));

    private static ClientBillingSummary Summary(IReadOnlyList<BillingUsageBreakdownRow> charged, IReadOnlyList<ProviderDirectUsageRow> uncharged,
        bool hasAccess = true, IReadOnlyList<BillingEntitlementRow>? entitlements = null) =>
        Blank<ClientBillingSummary>() with { Usage = charged, ProviderDirectUsage = uncharged, HasAccess = hasAccess, Entitlements = entitlements ?? [] };

    [Fact]
    public async Task Over_limits_lists_only_what_the_client_uses_beyond_the_plan()
    {
        // QA t2-billing-11: a downgrade to 1 seat left 3 seats in use without a word.
        Plan(new()
        {
            [EntitlementKeys.MaxUsers] = "1", [EntitlementKeys.MaxChannelConnections] = EntitlementCatalog.Unlimited,
            [EntitlementKeys.MaxWhatsAppNumbers] = "1", [EntitlementKeys.AiAssistant] = "false"
        });
        _clients.Setup(c => c.CountSeatsInUseAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _integrations.Setup(i => i.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration>
        {
            new() { Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected },
            new() { Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected },
        });
        _bots.Setup(b => b.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Bot> { new() { Status = BotStatus.Active } });

        var over = await Sut().OverLimitsAsync(ClientId);

        Assert.Equal(new[]
        {
            new PlanQuotaRow(EntitlementKeys.MaxUsers, 3, 1),
            new PlanQuotaRow(EntitlementKeys.MaxAiBots, 1, 0), // an active bot on a plan without the AI assistant
        }, over);
    }

    [Fact]
    public async Task An_ended_plan_shows_its_own_limits_not_zero()
    {
        // QA t2-billing-15: an expired trial read "Seats 1 of 0" (every enforced limit is 0 without an active plan).
        Plan(new(), hasAccess: false);
        _clients.Setup(c => c.CountSeatsInUseAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _integrations.Setup(i => i.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration>());
        _bots.Setup(b => b.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Bot>());
        var planValues = new List<BillingEntitlementRow>
        {
            new(EntitlementKeys.AiAssistant, "AI", "feature", "true"),
            new(EntitlementKeys.MaxUsers, "Seats", "limit", "3"),
            new(EntitlementKeys.MaxChannelConnections, "Channels", "limit", EntitlementCatalog.Unlimited),
            new(EntitlementKeys.MaxWhatsAppNumbers, "WhatsApp", "limit", "1"),
            new(EntitlementKeys.MaxAiBots, "Bots", "limit", "1"),
        };

        var usage = await Sut().GetAsync(ClientId, Summary([], [], hasAccess: false, entitlements: planValues));

        Assert.Equal(new[]
        {
            new PlanQuotaRow(EntitlementKeys.MaxUsers, 1, 3),
            new PlanQuotaRow(EntitlementKeys.MaxChannelConnections, 0, null),
            new PlanQuotaRow(EntitlementKeys.MaxWhatsAppNumbers, 0, 1),
            new PlanQuotaRow(EntitlementKeys.MaxAiBots, 0, 1),
        }, usage.Quotas);
        Assert.True(usage.AiIncluded);
    }

    [Fact]
    public async Task Counts_seats_channels_whatsapp_and_active_bots_against_the_plan()
    {
        Plan(new()
        {
            [EntitlementKeys.AiAssistant] = "true",
            [EntitlementKeys.MaxUsers] = "5",
            [EntitlementKeys.MaxChannelConnections] = EntitlementCatalog.Unlimited,
            [EntitlementKeys.MaxWhatsAppNumbers] = "1",
            [EntitlementKeys.MaxAiBots] = "2",
        });
        _clients.Setup(c => c.CountSeatsInUseAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _tokens.Setup(t => t.CountOpenInvitesAsync(ClientId, It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _integrations.Setup(i => i.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration>
        {
            new() { Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected },
            new() { Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected },
            new() { Channel = ChannelType.Facebook, Status = ChannelIntegrationStatus.Disconnected },
        });
        _bots.Setup(b => b.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Bot>
        {
            new() { Status = BotStatus.Active }, new() { Status = BotStatus.Inactive },
        });

        var usage = await Sut().GetAsync(ClientId, Summary([], []));

        Assert.Equal(new[]
        {
            new PlanQuotaRow(EntitlementKeys.MaxUsers, 3, 5),                 // users + open invites
            new PlanQuotaRow(EntitlementKeys.MaxChannelConnections, 2, null), // unlimited
            new PlanQuotaRow(EntitlementKeys.MaxWhatsAppNumbers, 1, 1),
            new PlanQuotaRow(EntitlementKeys.MaxAiBots, 1, 2),                // active bots only
        }, usage.Quotas);
        Assert.True(usage.AiIncluded);
    }

    [Fact]
    public async Task Without_ai_the_bots_limit_is_left_out()
    {
        Plan(new() { [EntitlementKeys.MaxUsers] = "3" });
        _integrations.Setup(i => i.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration>());
        _bots.Setup(b => b.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Bot>());

        var usage = await Sut().GetAsync(ClientId, Summary([], []));

        Assert.DoesNotContain(usage.Quotas, q => q.Key == EntitlementKeys.MaxAiBots);
        Assert.Equal(0, usage.Quotas.Single(q => q.Key == EntitlementKeys.MaxWhatsAppNumbers).Limit);   // missing key = not in the plan
        Assert.False(usage.AiIncluded);
    }

    [Fact]
    public async Task Messages_and_ai_replies_add_charged_and_uncharged_usage()
    {
        Plan(new() { [EntitlementKeys.AiAssistant] = "true" });
        _integrations.Setup(i => i.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration>());
        _bots.Setup(b => b.ListForClientReadOnlyAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Bot>());
        var summary = Summary(
            [
                new(BillableEventTypes.WhatsAppServiceMessage, "", 10, 10, 1.50m),
                new(BillableEventTypes.TelegramMessage, "", 4, 4, 0.20m),
                new(BillableEventTypes.AiReply, "", 6, 6, 0.60m),
                new(BillableEventTypes.WhatsAppNumberMonth, "", 1, 1, 5m),   // a monthly fee, not a message
            ],
            [
                new(BillableEventTypes.WhatsAppMarketingMessage, "", "", 3, 3, ProviderBillingResponsibility.CustomerDirect, ChannelType.WhatsApp),
            ]);

        var usage = await Sut().GetAsync(ClientId, summary);

        Assert.Equal((17, 1.70m, 6, 0.60m), (usage.Messages, usage.MessagesCharged, usage.AiReplies, usage.AiRepliesCharged));
    }

    [Theory]
    [InlineData(BillableEventTypes.WhatsAppUtilityMessage, true)]
    [InlineData(BillableEventTypes.SmsSegment, true)]
    [InlineData(BillableEventTypes.AiReply, false)]
    [InlineData(BillableEventTypes.VoiceMinute, false)]
    [InlineData(BillableEventTypes.AgentSeatMonth, false)]
    public void Which_event_types_are_messages(string eventType, bool message) =>
        Assert.Equal(message, BillableEventTypes.IsMessage(eventType));
}
