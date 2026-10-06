namespace PlasticSurgery.Common.Enums;

/// <summary>The provider side of a usage record (BillingUsageRecord.ProviderOutcome), separate from the money.</summary>
public static class ProviderOutcome
{
    /// <summary>Sent; the provider hasn't said yet whether it counts.</summary>
    public const string Pending = "pending";
    /// <summary>It counts at the provider (e.g. a WhatsApp template was delivered).</summary>
    public const string Billable = "billable";
    /// <summary>It never will (failed, rejected).</summary>
    public const string NotBillable = "not_billable";
}
