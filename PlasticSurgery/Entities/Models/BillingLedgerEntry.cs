using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

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
