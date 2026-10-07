using System.Globalization;
using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>
/// The one place that answers "may this clinic do X?" — evaluated from its subscription status and plan, never by
/// comparing plan names. Rules:
///   * billing off (Billing:Enabled = false) -> everything allowed, no limits (today's behaviour)
///   * active, or past_due within the grace period -> the plan's entitlements
///   * no subscription, expired, cancelled, or past_due beyond grace -> data stays viewable, but no sending,
///     campaigns or AI, and every limit is 0
/// Missing keys mean off / 0. A limit of null means unlimited.
/// </summary>
public sealed class ClinicEntitlements
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public ClinicEntitlements(bool billingEnabled, bool hasAccess, string? subscriptionStatus, string? planCode, string? planName,
        DateTimeOffset? periodEnd, IReadOnlyDictionary<string, string> values)
    {
        BillingEnabled = billingEnabled;
        HasAccess = hasAccess;
        SubscriptionStatus = subscriptionStatus;
        PlanCode = planCode;
        PlanName = planName;
        CurrentPeriodEnd = periodEnd;
        _values = values;
    }

    public static ClinicEntitlements Unrestricted { get; } =
        new(false, true, null, null, null, null, new Dictionary<string, string>());

    public bool BillingEnabled { get; }
    /// <summary>The subscription currently grants its plan (active, or past due within grace).</summary>
    public bool HasAccess { get; }
    public string? SubscriptionStatus { get; }
    public string? PlanCode { get; }
    public string? PlanName { get; }
    public DateTimeOffset? CurrentPeriodEnd { get; }

    public bool CanSendMessages => !BillingEnabled || HasAccess;
    public bool CanUseCampaigns => IsEnabled(EntitlementKeys.Campaigns);
    public bool CanUseAiAgent => IsEnabled(EntitlementKeys.AiAgent);
    public bool CanUseApi => IsEnabled(EntitlementKeys.ApiAccess);
    public bool CanUseAdvancedReporting => IsEnabled(EntitlementKeys.AdvancedReporting);
    public int? MaximumAgents => Limit(EntitlementKeys.MaxAgents);
    public int? MaximumWhatsAppNumbers => Limit(EntitlementKeys.MaxWhatsAppNumbers);
    public int? MaximumChannelConnections => Limit(EntitlementKeys.MaxChannelConnections);

    public bool IsEnabled(string featureKey) =>
        !BillingEnabled || (HasAccess && _values.TryGetValue(featureKey, out var v) && v == "true");

    /// <summary>null = unlimited.</summary>
    public int? Limit(string limitKey)
    {
        if (!BillingEnabled) return null;
        if (!HasAccess) return 0;
        if (!_values.TryGetValue(limitKey, out var v)) return 0;
        return v == EntitlementCatalog.Unlimited ? null : int.Parse(v, CultureInfo.InvariantCulture);
    }

    /// <summary>The plan's raw values, for display.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;
}
