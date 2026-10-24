using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Reports;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Reports;
using Omni.App.Entities.Responses.Reports;
using Omni.App.Persistence.Contracts.Reports;

namespace Omni.App.Business.Services.Reports;

public class ReportService : IReportService
{
    private readonly IReportRepository _reports;
    private readonly IEntitlementService _entitlements;

    public ReportService(IReportRepository reports, IEntitlementService entitlements)
    {
        _reports = reports;
        _entitlements = entitlements;
    }

    public async Task<BasicReportResponse> GetBasicAsync(Guid clientId, DateOnly? from, DateOnly? to, TimeZoneInfo tz,
        CancellationToken ct = default)
    {
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Reporting, ct);
        var plan = await _entitlements.GetAsync(clientId, ct);
        var (start, end) = ReportPeriod.ToUtcRange(from, to, tz);

        var channels = await _reports.GetChannelActivityAsync(clientId, start, end, ct);
        var bySource = await _reports.CountNewContactsBySourceAsync(clientId, start, end, ct);
        var byStage = await _reports.CountNewContactsByStageAsync(clientId, start, end, ct);
        var bookings = plan.CanUseBookings ? await _reports.GetBookingCountsAsync(clientId, start, end, ct) : null;
        var campaigns = plan.CanUseCampaigns
            ? await _reports.ListCampaignsAsync(clientId, start, end, ReportLimits.CampaignRows, ct)
            : null;

        return new BasicReportResponse(
            channels.Sum(c => c.NewConversations), channels.Sum(c => c.Received), channels.Sum(c => c.Sent), channels,
            bySource.Sum(s => s.Count), bySource, byStage, bookings, campaigns);
    }

    public async Task<AdvancedReportResponse> GetAdvancedAsync(Guid clientId, DateOnly? from, DateOnly? to, TimeZoneInfo tz,
        CancellationToken ct = default)
    {
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.AdvancedReporting, ct);
        var (start, end) = ReportPeriod.ToUtcRange(from, to, tz);

        var waits = await _reports.ListWaitsAsync(clientId, start, end, ct);
        var replies = await _reports.GetReplyCountsAsync(clientId, start, end, ct);
        var buckets = await _reports.GetMessageBucketsAsync(clientId, start, end, ct);
        var usage = await _reports.GetSettledUsageAsync(clientId, start, end, ct);

        var byMonth = from is null || to is null || to.Value.DayNumber - from.Value.DayNumber + 1 > ReportLimits.MaxDailyActivityDays;
        return new AdvancedReportResponse(ResponseTimes(waits), replies, Activity(buckets, from, to, tz, byMonth), byMonth, usage);
    }

    /// <summary>Median and average time to the first reply of each answered wait, overall and by who answered.</summary>
    public static ResponseTimeStats ResponseTimes(IReadOnlyList<ReportWait> waits)
    {
        var answered = waits.Where(w => w.RepliedAt is not null).ToList();
        TimeSpan Took(ReportWait w) => w.RepliedAt!.Value - w.WaitingSince;
        var times = answered.Select(Took).ToList();
        var fast = TimeSpan.FromMinutes(ReportLimits.FastReplyMinutes);

        return new ResponseTimeStats(
            answered.Count,
            waits.Count - answered.Count,
            Median(times),
            times.Count == 0 ? null : TimeSpan.FromTicks((long)times.Average(t => t.Ticks)),
            times.Count(t => t <= fast),
            Median(answered.Where(w => w.RepliedByAi).Select(Took).ToList()),
            Median(answered.Where(w => !w.RepliedByAi).Select(Took).ToList()));
    }

    private static TimeSpan? Median(List<TimeSpan> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : TimeSpan.FromTicks((values[mid - 1].Ticks + values[mid].Ticks) / 2);
    }

    /// <summary>Folds the 15-minute UTC slots into viewer-local days (every day of the period, empty ones too) or months
    /// (from the period's first month, or the first with messages, to its last).</summary>
    public static IReadOnlyList<ReportActivityRow> Activity(IReadOnlyList<ReportMessageBucket> buckets, DateOnly? from, DateOnly? to,
        TimeZoneInfo tz, bool byMonth)
    {
        DateOnly Key(DateTimeOffset slot)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(slot, tz).DateTime);
            return byMonth ? new DateOnly(day.Year, day.Month, 1) : day;
        }

        var totals = buckets.GroupBy(b => Key(b.SlotStart))
            .ToDictionary(g => g.Key, g => (Received: g.Sum(b => b.Received), Sent: g.Sum(b => b.Sent)));
        if (totals.Count == 0 && (from is null || to is null)) return [];

        DateOnly Floor(DateOnly d) => byMonth ? new DateOnly(d.Year, d.Month, 1) : d;
        var first = Floor(from ?? totals.Keys.Min());
        var last = Floor(to ?? totals.Keys.Max());
        if (totals.Count > 0)
        {
            if (totals.Keys.Min() < first) first = totals.Keys.Min();
            if (totals.Keys.Max() > last) last = totals.Keys.Max();
        }

        var rows = new List<ReportActivityRow>();
        for (var d = first; d <= last; d = byMonth ? d.AddMonths(1) : d.AddDays(1))
        {
            var (received, sent) = totals.GetValueOrDefault(d);
            rows.Add(new ReportActivityRow(d, received, sent));
        }
        return rows;
    }
}
