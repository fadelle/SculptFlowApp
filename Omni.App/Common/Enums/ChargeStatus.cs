namespace Omni.App.Common.Enums;

/// <summary>Omni's money side of a usage record (BillingUsageRecord.ChargeStatus).</summary>
public static class ChargeStatus
{
    public const string Reserved = "reserved";
    public const string Settled = "settled";
    public const string Released = "released";
    public const string Failed = "failed";
    /// <summary>Omni doesn't bill this usage (the customer or an external provider pays the provider, or there's
    /// no provider fee, and no Omni usage fee applies). Recorded for analytics only.</summary>
    public const string NotCharged = "not_charged";
}
