using Omni.App.Entities.Dtos.Reports;

namespace Omni.App.Entities.Responses.Reports;

/// <summary>The Reports page's advanced sections for one period. Activity is by viewer-local day, or by month when
/// ActivityByMonth (long or open-ended periods); Usage is what Omni charged.</summary>
public sealed record AdvancedReportResponse(
    ResponseTimeStats ResponseTimes,
    ReportReplyCounts Replies,
    IReadOnlyList<ReportActivityRow> Activity,
    bool ActivityByMonth,
    IReadOnlyList<ReportUsageRow> Usage);
