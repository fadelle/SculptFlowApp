using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Billing;

/// <summary>Paid add-ons: the catalog (billing.addons) and what each client has (billing.client_addons).</summary>
public interface IBillingAddonRepository
{
    Task<IReadOnlyList<Addon>> ListCatalogReadOnlyAsync(CancellationToken ct = default);

    /// <summary>Tracked, for a change; null when unknown.</summary>
    Task<Addon?> GetAsync(string code, CancellationToken ct = default);

    void Add(Addon addon);

    /// <summary>How many clients have each add-on (by add-on id).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountClientsByAddonAsync(CancellationToken ct = default);

    /// <summary>The client's add-ons with their catalog row, read-only.</summary>
    Task<IReadOnlyList<ClientAddon>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The client's row of this add-on (tracked), or null.</summary>
    Task<ClientAddon?> GetForClientAsync(Guid clientId, Guid addonId, CancellationToken ct = default);

    void AddForClient(ClientAddon clientAddon);

    void RemoveForClient(ClientAddon clientAddon);
}
