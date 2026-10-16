using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Billing;

/// <summary>billing.ledger_entries (append-only).</summary>
public interface IBillingLedgerRepository
{
    void Add(BillingLedgerEntry entry);

    Task<BillingLedgerEntry?> FindByKeyReadOnlyAsync(Guid clientId, string idempotencyKey, CancellationToken ct = default);

    Task<bool> KeyExistsAsync(Guid clientId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Newest posting first.</summary>
    Task<(IReadOnlyList<BillingLedgerEntry> Items, int Total)> ListAsync(Guid clientId, int skip, int take, CancellationToken ct = default);

    Task<decimal> SumAsync(Guid clientId, string balanceType, CancellationToken ct = default);

    /// <summary>Sum of every entry of this type created in [from, to), all clients.</summary>
    Task<decimal> SumOfTypeAsync(string entryType, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
