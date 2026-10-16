namespace Omni.App.Entities.Dtos.Billing;

// ---------------------------------------------------------------------------------------------------------------
// Client-facing (GET /api/billing/*, Settings → Billing). Never carries provider names, provider costs or margins.
// ---------------------------------------------------------------------------------------------------------------

public record BillingEntitlementRow(string Key, string Label, string Kind, string Value);
