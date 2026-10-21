using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Dtos.Dashboard;

namespace Omni.App.Pages.Dashboard;

/// <summary>Page 2 — Interested People.</summary>
public class ContactsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IDashboardService _dashboard;
    private readonly IContactService _contacts;
    private readonly IConfigManager _config;

    public ContactsModel(ICurrentClientContext clientContext, IDashboardService dashboard, IContactService contacts, IConfigManager config)
    {
        _config = config;
        _clientContext = clientContext;
        _dashboard = dashboard;
        _contacts = contacts;
    }

    [BindProperty(SupportsGet = true)]
    public string? Stage { get; set; }

    /// <summary>Set by the contact page after a privacy delete.</summary>
    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    // Named PageNumber, not Page — PageModel already declares a Page() method, and a same-named
    // property would just hide it (harmless but a compiler warning worth avoiding).
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public int PageSize => _config.DashboardContactsPageSize;

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<DashboardContactRow> Contacts { get; private set; } = Array.Empty<DashboardContactRow>();
    public int TotalCount { get; private set; }
    /// <summary>The client's pipeline stages in order, labelled (stage filter and badges).</summary>
    public IReadOnlyList<PipelineStageOption> Stages { get; private set; } = Array.Empty<PipelineStageOption>();
    /// <summary>Every source value this client's contacts actually have, for the source filter.</summary>
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

        var (items, totalCount) = await _dashboard.GetContactsAsync(client.Id, Stage, Search, Source, skip, PageSize, ct);
        Contacts = items;
        TotalCount = totalCount;
        AllSources = await _contacts.GetDistinctSourcesAsync(client.Id, ct);
        Stages = await _contacts.ListStagesAsync(client.Id, ct);
    }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
