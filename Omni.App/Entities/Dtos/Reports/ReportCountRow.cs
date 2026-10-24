namespace Omni.App.Entities.Dtos.Reports;

/// <summary>A count for one value (a contact source, a stage key, …). Key is null for "not set".</summary>
public sealed record ReportCountRow(string? Key, int Count);
