namespace Omni.App.Entities.Responses.Reports;

/// <summary>Messages received and sent on one viewer-local day (or in one month, see AdvancedReportResponse).</summary>
public sealed record ReportActivityRow(DateOnly Start, int Received, int Sent);
