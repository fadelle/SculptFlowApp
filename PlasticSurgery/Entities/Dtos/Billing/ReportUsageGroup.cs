namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>Provider-billable usage across clinics for the revenue report.</summary>
public record ReportUsageGroup(Guid ClinicId, string Channel, string EventType, string ProviderBilling, int Count, decimal Quantity,
    decimal Revenue, decimal Cost);
