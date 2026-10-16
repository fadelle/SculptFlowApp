using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Business.Contracts.Services.Leads;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Dashboard;

namespace Omni.App.Pages.Dashboard;

/// <summary>Page 2 — Interested People.</summary>
public class LeadsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IDashboardService _dashboard;
    private readonly ILeadService _leads;
    private readonly IConfigManager _config;

    public LeadsModel(ICurrentClientContext clientContext, IDashboardService dashboard, ILeadService leads, IConfigManager config)
    {
        _config = config;
        _clientContext = clientContext;
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

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<DashboardLeadRow> Leads { get; private set; } = Array.Empty<DashboardLeadRow>();
    public int TotalCount { get; private set; }
    public IReadOnlyList<string> AllStatuses { get; } = LeadStatus.All.OrderBy(s => s).ToList();
    /// <summary>Every source value this client's leads actually have, for the source filter.</summary>
    public IReadOnlyList<string> AllSources { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        PageNumber = Math.Max(PageNumber, 1);
        var skip = (PageNumber - 1) * PageSize;

        var (items, totalCount) = await _dashboard.GetLeadsAsync(client.Id, Status, Search, Source, skip, PageSize, ct);
        Leads = items;
        TotalCount = totalCount;
        AllSources = await _leads.GetDistinctSourcesAsync(client.Id, ct);
    }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
