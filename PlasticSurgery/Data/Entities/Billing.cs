namespace PlasticSurgery.Data.Entities;

/// <summary>The clinic's one prepaid billing account. Balances are a cache of the ledger
/// (<see cref="BillingLedgerEntry"/>): every change to them is posted there in the same transaction.
/// Spendable = WalletBalance + IncludedCreditBalance - ReservedAmount. Only Billing/BillingService changes these
/// columns, always under a row lock (select ... for update).</summary>
public class BillingAccount
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>Prepaid money. Only a settlement that costs more than was reserved can take it below zero.</summary>
    public decimal WalletBalance { get; set; }
    /// <summary>What is left of the plan's included usage credit for the current period. Never negative.</summary>
    public decimal IncludedCreditBalance { get; set; }
    /// <summary>Held for usage that was reserved but isn't settled or released yet (pooled across credit and wallet).</summary>
    public decimal ReservedAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public decimal Spendable => WalletBalance + IncludedCreditBalance - ReservedAmount;
}

/// <summary>A named price list. ClinicId set = that clinic's custom pricing; IsDefault = the fallback card.
/// Lookup order for a billable event: clinic card, then the plan's card, then the default card.</summary>
public class BillingRateCard
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ClinicId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One price VERSION for one billable event type in one card, optionally narrowed by destination country,
/// operator, provider and provider billing responsibility (null = any). Never edited (a DB trigger enforces it): a
/// price change closes this row (EffectiveTo) and adds a new one, so usage that was rated with it keeps its price.</summary>
public class BillingRate
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public string EventType { get; set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-2 destination country, or null for any.</summary>
    public string? CountryCode { get; set; }
    public string? Operator { get; set; }
    public string? Provider { get; set; }
    /// <summary>One of <see cref="ProviderBillingResponsibility"/>, or null. Null rates price SculptFlow-funded usage
    /// (and give provider cost for reporting on any usage); a rate set to e.g. customer_direct is a SculptFlow usage
    /// fee for accounts whose customer pays the provider. A customer-paid account is never charged a null rate.</summary>
    public string? ProviderBilling { get; set; }
    /// <summary>What one unit is ("message", "segment", "minute", "token"...) — informational.</summary>
    public string Unit { get; set; } = "unit";
    /// <summary>What the upstream provider charges per unit (paid by whoever the usage's responsibility says).</summary>
    public decimal ProviderCost { get; set; }
    /// <summary>What SculptFlow charges the clinic per unit. Independent of ProviderCost.</summary>
    public decimal ClientRate { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One billable event (a CDR): what happened, who paid the provider for it, and what SculptFlow charged.
/// Recorded whether or not SculptFlow charges it. Two separate statuses:
///   ChargeStatus    (SculptFlow's money): reserved -> settled | released; failed = refused (no rate / not enough
///                   balance); not_charged = SculptFlow doesn't bill this usage — never touches wallet, credit or ledger.
///   ProviderOutcome (what happened at the provider): pending -> billable (e.g. delivered) | not_billable (failed).
/// Prices are snapshotted at rating time. A DB trigger stops a final record from changing except for its refund
/// columns and provider outcome.</summary>
public class BillingUsageRecord
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid BillingAccountId { get; set; }
    /// <summary>Stable identity of this one billable event (unique per clinic), e.g. "whatsapp:message:{messageId}".
    /// A retry or a duplicate callback with the same key finds this row instead of charging again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    /// <summary>The connected channel account (channel_integrations.id) the usage went through, when known.</summary>
    public Guid? ChannelIntegrationId { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public string? CountryCode { get; set; }
    public string? Operator { get; set; }
    public string? Provider { get; set; }
    /// <summary>Who paid the upstream provider: one of <see cref="ProviderBillingResponsibility"/> (snapshot).</summary>
    public string ProviderBilling { get; set; } = ProviderBillingResponsibility.PlatformFunded;

    public Guid? RateId { get; set; }
    public Guid? RateCardId { get; set; }
    /// <summary>Which level of the rate-card hierarchy priced it: one of <see cref="RateSource"/>.</summary>
    public string? RateSource { get; set; }
    public decimal? UnitProviderCost { get; set; }
    public decimal? UnitPrice { get; set; }
    /// <summary>Total upstream provider cost (UnitProviderCost x Quantity) when known, whoever paid it. On
    /// SculptFlow-funded usage, Amount - this = margin.</summary>
    public decimal? ProviderCost { get; set; }
    /// <summary>Total SculptFlow charges the clinic (UnitPrice x Quantity): the estimate while reserved, final once
    /// settled, 0 when not charged.</summary>
    public decimal? Amount { get; set; }
    public decimal ReservedAmount { get; set; }
    /// <summary>Settled amount paid from included credit.</summary>
    public decimal CreditAmount { get; set; }
    /// <summary>Settled amount paid from the wallet.</summary>
    public decimal WalletAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>SculptFlow's money side: one of <see cref="ChargeStatus"/>.</summary>
    public string ChargeStatus { get; set; } = Entities.ChargeStatus.Reserved;
    /// <summary>The provider side: one of <see cref="ProviderOutcome"/>.</summary>
    public string ProviderOutcome { get; set; } = Entities.ProviderOutcome.Pending;
    /// <summary>For failed records: one of <see cref="UsageFailureReason"/>.</summary>
    public string? FailureReason { get; set; }
    public string? ReleaseReason { get; set; }

    public Guid? MessageId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? CampaignId { get; set; }
    /// <summary>One of <see cref="BillingSource"/>.</summary>
    public string Source { get; set; } = BillingSource.Channel;
    public string? Actor { get; set; }

    /// <summary>When the billable event happened — the date its rate was looked up for.</summary>
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ReservedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One immutable movement of one balance (wallet or included credit). The account's cached balance always
/// equals the sum of its entries. Updates are rejected by a DB trigger; corrections are new entries.</summary>
public class BillingLedgerEntry
{
    public Guid Id { get; set; }
    /// <summary>Posting order, assigned by the database (identity). Entries of one operation share CreatedAt.</summary>
    public long Seq { get; set; }
    public Guid ClinicId { get; set; }
    public Guid BillingAccountId { get; set; }
    /// <summary>One of <see cref="LedgerEntryType"/>.</summary>
    public string EntryType { get; set; } = string.Empty;
    /// <summary>One of <see cref="LedgerBalanceType"/>.</summary>
    public string BalanceType { get; set; } = LedgerBalanceType.Wallet;
    /// <summary>Signed: positive adds to the balance, negative takes from it.</summary>
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>Unique per clinic, so retrying the operation that posted it can never post it twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? UsageRecordId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Guid? PlanId { get; set; }
    /// <summary>One of <see cref="BillingSource"/>.</summary>
    public string Source { get; set; } = BillingSource.System;
    /// <summary>Who did it, for admin actions (the admin's email/name); null for automatic entries.</summary>
    public string? Actor { get; set; }
    public string? Reason { get; set; }
    /// <summary>External reference, e.g. the bank transfer or payment id behind a top-up.</summary>
    public string? Reference { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>SculptFlow's money side of a usage record (BillingUsageRecord.ChargeStatus).</summary>
public static class ChargeStatus
{
    public const string Reserved = "reserved";
    public const string Settled = "settled";
    public const string Released = "released";
    public const string Failed = "failed";
    /// <summary>SculptFlow doesn't bill this usage (the customer or an external provider pays the provider, or there's
    /// no provider fee, and no SculptFlow usage fee applies). Recorded for analytics only.</summary>
    public const string NotCharged = "not_charged";
}

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

/// <summary>Who pays the upstream communication provider for a connected channel account. Must match schema.sql.</summary>
public static class ProviderBillingResponsibility
{
    /// <summary>The customer pays the provider directly (e.g. their own WABA with their own Meta payment method).</summary>
    public const string CustomerDirect = "customer_direct";
    /// <summary>SculptFlow pays the provider (its Infobip account, an SMS aggregator) and charges the clinic its rates.</summary>
    public const string PlatformFunded = "platform_funded";
    /// <summary>The customer has its own account with another provider, which bills the customer.</summary>
    public const string ExternalProviderDirect = "external_provider_direct";
    /// <summary>No per-message provider fee for normal use (Telegram, Messenger, TikTok conversations...).</summary>
    public const string NoProviderUsageFee = "no_provider_usage_fee";

    public static readonly IReadOnlyList<string> All = new[] { CustomerDirect, PlatformFunded, ExternalProviderDirect, NoProviderUsageFee };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>Admin-facing wording.</summary>
    public static string AdminLabel(string value) => value switch
    {
        CustomerDirect => "Customer pays provider directly",
        PlatformFunded => "SculptFlow pays provider",
        ExternalProviderDirect => "Customer pays external provider",
        NoProviderUsageFee => "No provider usage fee",
        _ => value
    };
}

/// <summary>Per connected channel account billing settings (billing.channel_account_settings): overrides the
/// channel/provider defaults for who pays the provider and whether SculptFlow charges usage. Null = default.</summary>
public class ChannelAccountBillingSettings
{
    /// <summary>The connected account: channel_integrations.id (one row per account).</summary>
    public Guid ChannelIntegrationId { get; set; }
    public Guid ClinicId { get; set; }
    /// <summary>One of <see cref="ProviderBillingResponsibility"/>, or null for the default.</summary>
    public string? ProviderBilling { get; set; }
    /// <summary>Does SculptFlow charge usage on this account? Null = the default (yes only when SculptFlow pays the provider).</summary>
    public bool? OmniUsageBilling { get; set; }
    /// <summary>The override only holds while the account is connected through this provider (null = any), so a
    /// reconnect through another provider falls back to the defaults instead of inheriting the wrong arrangement.</summary>
    public string? AppliesToProvider { get; set; }
    public string? Reason { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class UsageFailureReason
{
    /// <summary>No rate card had a rate for this event — nothing is guessed, the usage isn't allowed.</summary>
    public const string RateNotFound = "rate_not_found";
    /// <summary>Included credit + wallet minus what's already reserved couldn't cover it.</summary>
    public const string InsufficientFunds = "insufficient_funds";
    /// <summary>The rate is in another currency than the clinic's account.</summary>
    public const string CurrencyMismatch = "currency_mismatch";
}

public static class RateSource
{
    public const string Client = "client";
    public const string Plan = "plan";
    public const string Default = "default";
}

public static class BillingSource
{
    /// <summary>Usage reported by a channel (WhatsApp send, delivery callback...).</summary>
    public const string Channel = "channel";
    /// <summary>A platform admin through the billing admin API.</summary>
    public const string Admin = "admin";
    /// <summary>The app itself (signup, subscription change requested in-app).</summary>
    public const string System = "system";
    /// <summary>The billing maintenance worker (renewals, expiry, stale reservations).</summary>
    public const string Worker = "worker";
}

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

public static class LedgerBalanceType
{
    public const string Wallet = "wallet";
    public const string IncludedCredit = "included_credit";
}
