using Omni.App.Entities.Responses.Reports;

namespace Omni.App.Business.Contracts.Services.Reports;

/// <summary>The Reports page. Periods are inclusive viewer-local dates in <c>tz</c> (nulls = open-ended, see ReportPeriod).</summary>
public interface IReportService
{
    /// <summary>Conversations, contacts, bookings (when the plan has bookings) and campaigns (when it has campaigns). Needs the
    /// plan's <c>reporting</c> feature (EntitlementDeniedException otherwise).</summary>
    Task<BasicReportResponse> GetBasicAsync(Guid clientId, DateOnly? from, DateOnly? to, TimeZoneInfo tz, CancellationToken ct = default);

    /// <summary>Response times, AI replies, daily (or monthly) activity and charged usage. Needs the plan's
    /// <c>advanced_reporting</c> feature.</summary>
    Task<AdvancedReportResponse> GetAdvancedAsync(Guid clientId, DateOnly? from, DateOnly? to, TimeZoneInfo tz, CancellationToken ct = default);
}
