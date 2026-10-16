namespace Omni.App.Entities.Dtos.Billing;

/// <summary>Provider-billable usage across clients for the revenue report.</summary>
public record ReportUsageGroup(Guid ClientId, string Channel, string EventType, string ProviderBilling, int Count, decimal Quantity,
    decimal Revenue, decimal Cost);
