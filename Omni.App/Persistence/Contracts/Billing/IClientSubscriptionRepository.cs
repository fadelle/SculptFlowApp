using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Billing;

/// <summary>billing.subscriptions (one per client).</summary>
public interface IClientSubscriptionRepository
{
    void Add(ClientSubscription subscription);

    /// <summary>Tracked, with Plan.</summary>
    Task<ClientSubscription?> GetWithPlanAsync(Guid clientId, CancellationToken ct = default);

    Task<ClientSubscription?> GetWithPlanReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Read-only, with Plan and its entitlements.</summary>
    Task<ClientSubscription?> GetWithEntitlementsReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Every subscription with its plan, keyed by client.</summary>
    Task<IReadOnlyDictionary<Guid, ClientSubscription>> MapAllWithPlanReadOnlyAsync(CancellationToken ct = default);

    /// <summary>Clients whose active period has ended, or that are past due.</summary>
    Task<IReadOnlyList<Guid>> ListDueClientIdsAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Active and past-due subscribers per plan.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSubscribersByPlanAsync(CancellationToken ct = default);
}
