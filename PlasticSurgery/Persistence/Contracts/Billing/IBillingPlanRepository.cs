using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>billing.plans and their entitlements.</summary>
public interface IBillingPlanRepository
{
    /// <summary>Read-only, with entitlements, by sort order then price.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListWithEntitlementsReadOnlyAsync(CancellationToken ct = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>Tracked, with entitlements.</summary>
    Task<SubscriptionPlan?> GetWithEntitlementsAsync(string code, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<SubscriptionPlan?> GetAsync(string code, CancellationToken ct = default);

    void Add(SubscriptionPlan plan);

    /// <summary>Explicit add: a set Guid key found through the navigation would be taken for an existing row.</summary>
    void AddEntitlement(SubscriptionPlanEntitlement entitlement);

    void RemoveEntitlement(SubscriptionPlanEntitlement entitlement);
}
