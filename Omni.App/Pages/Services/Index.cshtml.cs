using Omni.App.Controllers.Filters;
using Omni.App.Common.Statics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Requests.Catalog;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Pages.Services;

/// <summary>Services management — the client's structured service catalog (see ICatalogService). Adding and editing
/// happen in a popup on this page (Save handler; ?add=true or ?edit={id} opens it from a link). The client always comes
/// from the logged-in user (ICurrentClientContext); services are activated / deactivated, never deleted.</summary>
[RequirePlanFeature(EntitlementKeys.Bookings, EntitlementKeys.AiAssistant)]
public class IndexModel : PageModel
{
    private const string NotFoundKey = "services.errors.notFound";
    private const string ActivatedKey = "services.activated";
    private const string DeactivatedKey = "services.deactivated";
    private const string SavedKey = "services.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly ICatalogService _services;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, ICatalogService services, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _services = services;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<ServiceResponse> Services { get; private set; } = Array.Empty<ServiceResponse>();

    /// <summary>?add=true opens the popup empty on load.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Add { get; set; }

    /// <summary>?edit={id} opens the popup on that service on load; also the service the Save handler changes (null = new).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? Code { get; set; }
    [BindProperty] public int? DurationMinutes { get; set; }
    [BindProperty] public string? Description { get; set; }

    /// <summary>The popup is open on load (a link asked for it, or a save failed and shows what was posted).</summary>
    public bool FormOpen { get; private set; }
    public string? FormError { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return Page();

        ClientConfigured = true;
        Services = await _services.ListAsync(client.Id, activeOnly: false, ct);
        if (Edit is { } id)
        {
            var service = Services.FirstOrDefault(s => s.Id == id);
            if (service is null) return NotFound();
            (Name, Code, DurationMinutes, Description) = (service.Name, service.Code, service.BookingDuration, service.Description);
            FormOpen = true;
        }
        else
        {
            FormOpen = Add;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            if (Edit is null)
            {
                await _services.CreateAsync(new CreateServiceRequest(client.Id, Name, Code, Description, DurationMinutes), ct);
            }
            else if (await _services.UpdateAsync(client.Id, Edit.Value, new UpdateServiceRequest(Name, Code, Description, DurationMinutes), ct) is null)
            {
                ErrorMessage = _text[NotFoundKey].Value;
                return RedirectToPage(new { edit = (Guid?)null });
            }

            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage(new { edit = (Guid?)null });
        }
        catch (ArgumentException ex)
        {
            // Show the list again with the popup open on what was posted.
            ClientConfigured = true;
            Services = await _services.ListAsync(client.Id, activeOnly: false, ct);
            FormOpen = true;
            FormError = _text.Localize(ex);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var service = await _services.SetActiveAsync(client.Id, id, active, ct);
        if (service is null) ErrorMessage = _text[NotFoundKey].Value;
        else StatusMessage = _text[active ? ActivatedKey : DeactivatedKey, service.Name].Value;
        return RedirectToPage();
    }
}
