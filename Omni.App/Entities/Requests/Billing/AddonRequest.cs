namespace Omni.App.Entities.Requests.Billing;

/// <summary>PUT /api/platform-admin/billing/addons/{code}: creates or changes a catalog add-on. The entitlement is fixed once
/// the add-on exists.</summary>
public record AddonRequest(string? Name, string? EntitlementKey, decimal UnitPrice, bool IsActive = true, int SortOrder = 0);
