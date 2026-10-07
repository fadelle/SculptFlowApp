using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

public interface IBillingAccountRepository
{
    /// <summary>Creates the clinic's account if it doesn't exist yet (race-safe), then locks it FOR UPDATE until the
    /// transaction ends. Must run inside a transaction.</summary>
    Task<BillingAccount> LockAsync(Guid clinicId, string currency, CancellationToken ct = default);

    Task<BillingAccount?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Every account, keyed by clinic.</summary>
    Task<IReadOnlyDictionary<Guid, BillingAccount>> MapAllReadOnlyAsync(CancellationToken ct = default);

    void Add(BillingAccount account);
}
