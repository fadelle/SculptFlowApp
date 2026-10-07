namespace PlasticSurgery.Entities.Dtos.Billing;

/// <param name="Count">Usage that counted at the provider (delivered/billable), charged by SculptFlow or not.</param>
/// <param name="Revenue">SculptFlow usage revenue (charged, minus refunds).</param>
/// <param name="ProviderCost">Provider cost where known, whoever paid it (ProviderBilling says who).</param>
/// <param name="Margin">Revenue minus the provider cost SculptFlow paid itself.</param>
public record BillingReportRow(Guid ClinicId, string ClinicName, string Channel, string EventType, string ProviderBilling, int Count,
    decimal Quantity, decimal Revenue, decimal ProviderCost, decimal Margin);
