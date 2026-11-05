using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts.Ai;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Business.Services.Billing;

/// <summary>
/// Counts what the client uses of its plan the same way the limits are enforced: seats = active users + open invites
/// (InviteService/MemberService), channels = connected channel rows and WhatsApp numbers = the connected WhatsApp row
/// (EntitlementService.EnsureCanConnectChannelAsync), AI bots = active bots (BotService). Limits come from
/// <see cref="IEntitlementService"/> (plan + add-ons; 0 without an active plan). Messages and AI replies are this period's
/// usage rows, charged or not.
/// </summary>
public class PlanUsageService : IPlanUsageService
{
    private readonly IEntitlementService _entitlements;
    private readonly IClientRepository _clients;
    private readonly IAccountTokenRepository _tokens;
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IBotRepository _bots;
    private readonly TimeProvider _time;

    public PlanUsageService(IEntitlementService entitlements, IClientRepository clients, IAccountTokenRepository tokens,
        IChannelIntegrationRepository integrations, IBotRepository bots, TimeProvider time)
    {
        _entitlements = entitlements;
        _clients = clients;
        _tokens = tokens;
        _integrations = integrations;
        _bots = bots;
        _time = time;
    }

    public async Task<PlanUsageDto> GetAsync(Guid clientId, ClientBillingSummary summary, CancellationToken ct = default)
    {
        var plan = await _entitlements.GetAsync(clientId, ct);
        var seats = await _clients.CountSeatsInUseAsync(clientId, ct) + await _tokens.CountOpenInvitesAsync(clientId, _time.GetUtcNow(), null, ct);
        var connected = (await _integrations.ListForClientReadOnlyAsync(clientId, ct))
            .Where(c => c.Status == ChannelIntegrationStatus.Connected).ToList();
        var activeBots = (await _bots.ListForClientReadOnlyAsync(clientId, ct)).Count(b => b.Status == BotStatus.Active);

        // Without an active plan the enforced limits are all 0 ("Seats 1 of 0", QA t2-billing-15): show the plan's own limits.
        int? Limit(string key, int? enforced) => summary.HasAccess ? enforced : PlanLimit(summary, key);
        var quotas = new List<PlanQuotaRow>
        {
            new(EntitlementKeys.MaxUsers, seats, Limit(EntitlementKeys.MaxUsers, plan.MaximumUsers)),
            new(EntitlementKeys.MaxChannelConnections, connected.Count, Limit(EntitlementKeys.MaxChannelConnections, plan.MaximumChannelConnections)),
            new(EntitlementKeys.MaxWhatsAppNumbers, connected.Count(c => c.Channel == ChannelType.WhatsApp), Limit(EntitlementKeys.MaxWhatsAppNumbers, plan.MaximumWhatsAppNumbers)),
        };
        var aiIncluded = summary.HasAccess ? plan.CanUseAiAssistant : summary.Entitlements.Any(e => e.Key == EntitlementKeys.AiAssistant && e.Value == "true");
        if (aiIncluded) quotas.Add(new(EntitlementKeys.MaxAiBots, activeBots, Limit(EntitlementKeys.MaxAiBots, plan.MaximumAiBots)));

        // Charged usage (summary.Usage) and usage Omni didn't charge (summary.ProviderDirectUsage) both count.
        var charged = summary.Usage.Select(u => (u.EventType, u.Count, u.Amount));
        var uncharged = summary.ProviderDirectUsage.Select(u => (u.EventType, u.Count, Amount: 0m));
        var all = charged.Concat(uncharged).ToList();
        var messages = all.Where(u => BillableEventTypes.IsMessage(u.EventType)).ToList();
        var aiReplies = all.Where(u => u.EventType == BillableEventTypes.AiReply).ToList();

        return new PlanUsageDto(quotas,
            messages.Sum(u => u.Count), messages.Sum(u => u.Amount),
            aiReplies.Sum(u => u.Count), aiReplies.Sum(u => u.Amount),
            aiIncluded);
    }

    /// <summary>The subscription's plan's own limit (with add-ons), as the summary lists it: null = unlimited.</summary>
    private static int? PlanLimit(ClientBillingSummary summary, string key) =>
        summary.Entitlements.FirstOrDefault(e => e.Key == key)?.Value is { } value
            ? value == EntitlementCatalog.Unlimited ? null : int.TryParse(value, out var n) ? n : 0
            : 0;
}
