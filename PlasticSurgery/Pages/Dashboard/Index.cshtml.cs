using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Pages.Shared;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>
/// Page 1 — Dashboard. Calls the same application services the REST API controllers call
/// (IDashboardService etc.) rather than making an HTTP round-trip to its own API — same business
/// logic either way, without the extra network hop and JSON round-trip inside one process.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IDashboardService _dashboard;
    private readonly IWhatsAppHealthService _whatsAppHealth;

    public IndexModel(ICurrentClinicContext clinicContext, IDashboardService dashboard, IWhatsAppHealthService whatsAppHealth)
    {
        _clinicContext = clinicContext;
        _dashboard = dashboard;
        _whatsAppHealth = whatsAppHealth;
    }

    /// <summary>The period dropdown's choices, in display order (value, label).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Periods = new[]
    {
        ("this_week", "This week"),
        ("last_week", "Last week"),
        ("this_month", "This month"),
        ("last_month", "Last month"),
        ("7d", "Last 7 days"),
        ("30d", "Last 30 days"),
        ("all", "All time"),
        ("custom", "Custom range…"),
    };

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "this_month";

    /// <summary>Custom range, inclusive, as viewer-local dates.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public string? ClinicName { get; private set; }
    public bool ClinicConfigured { get; private set; }
    public Guid ClinicId { get; private set; }
    public DashboardSummaryResponse? Summary { get; private set; }
    public DashboardAttentionResponse? Attention { get; private set; }
    public IReadOnlyList<DashboardProcedureRow> Procedures { get; private set; } = Array.Empty<DashboardProcedureRow>();
    /// <summary>Human-readable description of the selected range, e.g. "Sep 1 – Sep 30, 2026".</summary>
    public string RangeLabel { get; private set; } = "All time";
    /// <summary>Null means "no WhatsApp connection yet" — the indicator shows a neutral state, not
    /// a warning, since an unconnected number isn't itself a problem for a brand-new clinic.</summary>
    public WhatsAppHealthResponse? WhatsAppHealth { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        ClinicId = clinic.Id;
        ClinicName = clinic.Name;

        var tz = ViewerTimeZone.Resolve(Request, clinic.Timezone);
        var (fromDate, toDate) = ResolveRange(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date);
        RangeLabel = fromDate is null || toDate is null
            ? "All time"
            : fromDate == toDate
                ? fromDate.Value.ToString("MMM d, yyyy")
                : $"{fromDate.Value:MMM d} – {toDate.Value:MMM d, yyyy}";

        // Viewer-local midnights -> UTC instants; the upper bound is exclusive (midnight after the last day).
        DateTimeOffset? from = fromDate is null ? null : LocalMidnightUtc(fromDate.Value, tz);
        DateTimeOffset? to = toDate is null ? null : LocalMidnightUtc(toDate.Value.AddDays(1), tz);

        Summary = await _dashboard.GetSummaryAsync(clinic.Id, from, to, ct);
        Procedures = await _dashboard.GetProcedureStatsAsync(clinic.Id, from, to, ct);
        Attention = await _dashboard.GetAttentionAsync(clinic.Id, ct);
        WhatsAppHealth = await _whatsAppHealth.GetHealthAsync(clinic.Id, ct);
    }

    /// <summary>Inclusive viewer-local start/end dates for the selected period (nulls = all time). Weeks start on Monday.</summary>
    private (DateOnly? From, DateOnly? To) ResolveRange(DateTime todayLocal)
    {
        var today = DateOnly.FromDateTime(todayLocal);
        var mondayOffset = ((int)today.DayOfWeek + 6) % 7; // Monday = 0 … Sunday = 6
        var thisMonday = today.AddDays(-mondayOffset);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        switch (Period)
        {
            case "this_week": return (thisMonday, today);
            case "last_week": return (thisMonday.AddDays(-7), thisMonday.AddDays(-1));
            case "this_month": return (firstOfMonth, today);
            case "last_month": return (firstOfMonth.AddMonths(-1), firstOfMonth.AddDays(-1));
            case "7d": return (today.AddDays(-6), today);
            case "30d": return (today.AddDays(-29), today);
            case "all": return (null, null);
            case "custom":
                if (From is null && To is null) return (null, null);
                var start = From ?? To!.Value;
                var end = To ?? today;
                return start <= end ? (start, end) : (end, start);
            default:
                Period = "this_month";
                return (firstOfMonth, today);
        }
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }
}
