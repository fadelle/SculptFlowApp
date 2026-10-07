using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Dashboard;
using PlasticSurgery.Business.Contracts.Services.Leads;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Dashboard;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>Page 2 — Interested People.</summary>
public class LeadsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IDashboardService _dashboard;
    private readonly ILeadService _leads;
    private readonly IConfigManager _config;

    public LeadsModel(ICurrentClinicContext clinicContext, IDashboardService dashboard, ILeadService leads, IConfigManager config)
    {
        _config = config;
        _clinicContext = clinicContext;
        _dashboard = dashboard;
        _leads = leads;
    }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    // Named PageNumber, not Page — PageModel already declares a Page() method, and a same-named
    // property would just hide it (harmless but a compiler warning worth avoiding).
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public int PageSize => _config.DashboardLeadsPageSize;

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<DashboardLeadRow> Leads { get; private set; } = Array.Empty<DashboardLeadRow>();
    public int TotalCount { get; private set; }
    public IReadOnlyList<string> AllStatuses { get; } = LeadStatus.All.OrderBy(s => s).ToList();
    /// <summary>Every source value this clinic's leads actually have, for the source filter.</summary>
    public IReadOnlyList<string> AllSources { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        PageNumber = Math.Max(PageNumber, 1);
        var skip = (PageNumber - 1) * PageSize;

        var (items, totalCount) = await _dashboard.GetLeadsAsync(clinic.Id, Status, Search, Source, skip, PageSize, ct);
        Leads = items;
        TotalCount = totalCount;
        AllSources = await _leads.GetDistinctSourcesAsync(clinic.Id, ct);
    }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
