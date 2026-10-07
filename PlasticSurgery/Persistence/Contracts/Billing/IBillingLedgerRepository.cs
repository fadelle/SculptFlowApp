using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>billing.ledger_entries (append-only).</summary>
public interface IBillingLedgerRepository
{
    void Add(BillingLedgerEntry entry);

    Task<BillingLedgerEntry?> FindByKeyReadOnlyAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default);

    Task<bool> KeyExistsAsync(Guid clinicId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Newest posting first.</summary>
    Task<(IReadOnlyList<BillingLedgerEntry> Items, int Total)> ListAsync(Guid clinicId, int skip, int take, CancellationToken ct = default);

    Task<decimal> SumAsync(Guid clinicId, string balanceType, CancellationToken ct = default);

    /// <summary>Sum of every entry of this type created in [from, to), all clinics.</summary>
    Task<decimal> SumOfTypeAsync(string entryType, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
