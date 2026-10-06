using PlasticSurgery.Entities.Dtos.Billing;

namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for BillingLedgerEntry.EntryType — must match schema.sql's CHECK constraint.</summary>
public static class LedgerEntryType
{
    public const string WalletTopUp = "wallet_top_up";
    public const string UsageDebit = "usage_debit";
    public const string IncludedCreditConsumption = "included_credit_consumption";
    public const string UsageRefund = "usage_refund";
    public const string SubscriptionCharge = "subscription_charge";
    public const string IncludedCreditGrant = "included_credit_grant";
    public const string IncludedCreditExpiry = "included_credit_expiry";
    public const string ManualAdjustment = "manual_adjustment";
}
