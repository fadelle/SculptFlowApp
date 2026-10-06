namespace PlasticSurgery.Entities.Dtos.Billing;

public record BillingUsageBreakdownRow(string EventType, string Label, int Count, decimal Quantity, decimal Amount);
