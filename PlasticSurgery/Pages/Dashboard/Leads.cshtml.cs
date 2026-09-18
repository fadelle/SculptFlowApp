using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>Page 2 — Interested People.</summary>
public class LeadsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IDashboardService _dashboard;

    public LeadsModel(ICurrentClinicContext clinicContext, IDashboardService dashboard)
    {
        _clinicContext = clinicContext;
        _dashboard = dashboard;
    }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    // Named PageNumber, not Page — PageModel already declares a Page() method, and a same-named
    // property would just hide it (harmless but a compiler warning worth avoiding).
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 25;

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<DashboardLeadRow> Leads { get; private set; } = Array.Empty<DashboardLeadRow>();
    public int TotalCount { get; private set; }
    public IReadOnlyList<string> AllStatuses { get; } = LeadStatus.All.OrderBy(s => s).ToList();

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

        var (items, totalCount) = await _dashboard.GetLeadsAsync(clinic.Id, Status, Search, skip, PageSize, ct);
        Leads = items;
        TotalCount = totalCount;
    }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
