using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Billing;

public interface IBillingAccountRepository
{
    /// <summary>Creates the client's account if it doesn't exist yet (race-safe), then locks it FOR UPDATE until the
    /// transaction ends. Must run inside a transaction.</summary>
    Task<BillingAccount> LockAsync(Guid clientId, string currency, CancellationToken ct = default);

    Task<BillingAccount?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Every account, keyed by client.</summary>
    Task<IReadOnlyDictionary<Guid, BillingAccount>> MapAllReadOnlyAsync(CancellationToken ct = default);

    void Add(BillingAccount account);
}
