namespace Omni.App.Entities.Dtos.Reports;

/// <summary>Usage Omni charged in the period for one event type and currency (Amount = settled minus refunded).</summary>
public sealed record ReportUsageRow(string EventType, string Currency, int Count, decimal Quantity, decimal Amount);
