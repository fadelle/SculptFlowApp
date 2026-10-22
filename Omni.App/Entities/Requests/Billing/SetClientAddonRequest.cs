namespace Omni.App.Entities.Requests.Billing;

/// <summary>PUT /api/platform-admin/billing/clients/{id}/addons/{code}: the client's new number of units (0 removes the
/// add-on). Added units are charged now (Idempotency-Key header).</summary>
public record SetClientAddonRequest(int Quantity);
