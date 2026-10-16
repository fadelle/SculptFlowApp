namespace Omni.App.Entities.Responses.Billing;

public record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// ---------------------------------------------------------------------------------------------------------------
// Platform admin (/api/platform-admin/billing/*, X-Platform-Admin-Key), called by the Omni.Admin portal, which
// keeps a copy of these shapes (Omni.Admin/Billing/BillingApiContracts.cs). Internal: may show providers, costs
// and margins. Change a shape here → update that copy.
// ---------------------------------------------------------------------------------------------------------------
