namespace PlasticSurgery.Entities.Models;

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
