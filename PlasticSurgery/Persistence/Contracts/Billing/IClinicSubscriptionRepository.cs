using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>billing.subscriptions (one per clinic).</summary>
public interface IClinicSubscriptionRepository
{
    void Add(ClinicSubscription subscription);

    /// <summary>Tracked, with Plan.</summary>
    Task<ClinicSubscription?> GetWithPlanAsync(Guid clinicId, CancellationToken ct = default);

    Task<ClinicSubscription?> GetWithPlanReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only, with Plan and its entitlements.</summary>
    Task<ClinicSubscription?> GetWithEntitlementsReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Every subscription with its plan, keyed by clinic.</summary>
    Task<IReadOnlyDictionary<Guid, ClinicSubscription>> MapAllWithPlanReadOnlyAsync(CancellationToken ct = default);

    /// <summary>Clinics whose active period has ended, or that are past due.</summary>
    Task<IReadOnlyList<Guid>> ListDueClinicIdsAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Active and past-due subscribers per plan.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSubscribersByPlanAsync(CancellationToken ct = default);
}
