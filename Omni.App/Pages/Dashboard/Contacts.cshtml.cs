using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Catalog;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Dtos.Dashboard;

namespace Omni.App.Pages.Dashboard;

/// <summary>Page 2 — Interested People. "Add" opens a popup that adds a contact by hand (QA t3-inbox-12; <c>?add=true</c> opens it
/// without script), then goes to the new contact's page.</summary>
public class ContactsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IDashboardService _dashboard;
    private readonly IContactService _contacts;
    private readonly IConfigManager _config;
    private readonly ICatalogService _catalog;
    private readonly IStringLocalizer _text;

    private const string AddedKey = "contacts.add.done";

    public ContactsModel(ICurrentClientContext clientContext, IDashboardService dashboard, IContactService contacts, IConfigManager config,
        ICatalogService catalog, IStringLocalizer text)
    {
        _catalog = catalog;
        _text = text;
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

    /// <summary>The client's active services, for the new contact's optional service.</summary>
    public IReadOnlyList<ServiceResponse> ActiveServices { get; private set; } = Array.Empty<ServiceResponse>();

    [BindProperty] public string? NewName { get; set; }
    [BindProperty] public string? NewPhone { get; set; }
    [BindProperty] public string? NewEmail { get; set; }
    [BindProperty] public Guid? NewServiceId { get; set; }
    [BindProperty] public string? NewNotes { get; set; }

    /// <summary>The add popup is open on load (<c>?add=true</c>, or a save that failed: then with <see cref="AddError"/>).</summary>
    public bool AddFormOpen { get; private set; }
    public string? AddError { get; private set; }

    public async Task OnGetAsync(bool add, CancellationToken ct)
    {
        await LoadAsync(ct);
        AddFormOpen = add && ClientConfigured;
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        try
        {
            var contact = await _contacts.AddManualAsync(client.Id, new ManualContactRequest(NewName, NewPhone, NewEmail, NewServiceId, NewNotes), ct);
            StatusMessage = _text[AddedKey].Value;
            return LocalRedirect($"/dashboard/contacts/{contact.Id}");
        }
        catch (ArgumentException ex)
        {
            AddError = _text.Localize(ex);
            AddFormOpen = true;
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
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
        ActiveServices = await _catalog.ListAsync(client.Id, activeOnly: true, ct);
    }

    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
