using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Dashboard;
using Omni.App.Entities.Responses.Dashboard;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Pages.Dashboard;

/// <summary>
/// Page 1 — Dashboard. Calls the same application services the REST API controllers call
/// (IDashboardService etc.) rather than making an HTTP round-trip to its own API — same business
/// logic either way, without the extra network hop and JSON round-trip inside one process.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IDashboardService _dashboard;

    private readonly IWhatsAppHealthService _whatsAppHealth;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IDashboardService dashboard, IWhatsAppHealthService whatsAppHealth, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _dashboard = dashboard;
        _whatsAppHealth = whatsAppHealth;
        _text = text;
    }

    /// <summary>The period dropdown's choices, in display order (the page names each one).</summary>
    public static IReadOnlyList<string> Periods => ReportPeriod.All;

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "this_month";

    /// <summary>Custom range, inclusive, as viewer-local dates.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public string? ClientName { get; private set; }
    public bool ClientConfigured { get; private set; }
    public Guid ClientId { get; private set; }
    public DashboardSummaryResponse? Summary { get; private set; }
    public DashboardAttentionResponse? Attention { get; private set; }
    public IReadOnlyList<DashboardServiceRow> Services { get; private set; } = Array.Empty<DashboardServiceRow>();
    /// <summary>Human-readable description of the selected range, e.g. "Sep 1 – Sep 30, 2026".</summary>
    public string RangeLabel { get; private set; } = string.Empty;
    /// <summary>Null means "no WhatsApp connection yet" — the indicator shows a neutral state, not
    /// a warning, since an unconnected number isn't itself a problem for a brand-new client.</summary>
    public WhatsAppHealthResponse? WhatsAppHealth { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        ClientId = client.Id;
        ClientName = client.Name;

        var tz = ViewerTimeZone.Resolve(Request, client.Timezone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date);
        (Period, var fromDate, var toDate) = ReportPeriod.Resolve(Period, From, To, today);
        RangeLabel = ReportPeriod.Label(_text, fromDate, toDate);

        // Viewer-local midnights -> UTC instants; the upper bound is exclusive (midnight after the last day).
        var (from, to) = ReportPeriod.ToUtcRange(fromDate, toDate, tz);

        Summary = await _dashboard.GetSummaryAsync(client.Id, from, to, ct);
        Services = await _dashboard.GetServiceStatsAsync(client.Id, from, to, ct);
        Attention = await _dashboard.GetAttentionAsync(client.Id, ct);
        WhatsAppHealth = await _whatsAppHealth.GetHealthAsync(client.Id, ct);
    }
}
