using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Requests.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>
/// Paid add-ons: extra units of a limit (users, channels, AI bots, WhatsApp numbers) on top of the client's plan. The
/// catalog (billing.addons) and each client's units (billing.client_addons) are managed from the admin portal; the client's
/// limit is its plan's value plus its units (AddonLimits). Added units are charged from the wallet at once (ledger
/// addon_charge, no proration); every period start charges the plan and the add-ons together (SubscriptionService).
/// Removing units refunds nothing.
/// </summary>
public interface IAddonService
{
    Task<IReadOnlyList<AddonResponse>> ListCatalogAsync(CancellationToken ct = default);

    /// <summary>Creates or changes a catalog add-on. ArgumentException for a bad code, name or price, an entitlement that
    /// isn't a limit, one that another add-on already raises, or changing an existing add-on's entitlement.</summary>
    Task<AddonResponse> SaveAsync(string code, AddonRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ClientAddonRow>> ListForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Sets the client's units of an add-on (0 removes it). Added units cost their price now: BillingDeniedException
    /// when the wallet can't pay. KeyNotFoundException for an unknown add-on; InvalidOperationException when the client has no
    /// active subscription or the add-on is switched off (only for adding). A repeated idempotency key changes nothing.</summary>
    Task<IReadOnlyList<ClientAddonRow>> SetQuantityAsync(Guid clientId, string code, int quantity, string idempotencyKey, string source,
        string? actor, CancellationToken ct = default);
}
