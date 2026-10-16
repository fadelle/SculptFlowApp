namespace Omni.App.Entities.Dtos.Billing;

/// <param name="Count">Usage that counted at the provider (delivered/billable), charged by Omni or not.</param>
/// <param name="Revenue">Omni usage revenue (charged, minus refunds).</param>
/// <param name="ProviderCost">Provider cost where known, whoever paid it (ProviderBilling says who).</param>
/// <param name="Margin">Revenue minus the provider cost Omni paid itself.</param>
public record BillingReportRow(Guid ClientId, string ClientName, string Channel, string EventType, string ProviderBilling, int Count,
    decimal Quantity, decimal Revenue, decimal ProviderCost, decimal Margin);
