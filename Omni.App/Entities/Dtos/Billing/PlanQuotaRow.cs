namespace Omni.App.Entities.Dtos.Billing;

/// <summary>One plan limit on the client's Billing page: how many are in use against the limit (with add-ons).
/// <see cref="Limit"/> null = unlimited; 0 = not in the plan.</summary>
public record PlanQuotaRow(string Key, int Used, int? Limit);
