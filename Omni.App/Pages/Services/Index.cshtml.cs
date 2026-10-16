using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Pages.Services;

/// <summary>Services management — the client's structured service catalog (see ICatalogService).
/// The client always comes from the logged-in user (ICurrentClientContext); services are activated /
/// deactivated, never deleted.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly ICatalogService _services;

    public IndexModel(ICurrentClientContext clientContext, ICatalogService services)
    {
        _clientContext = clientContext;
        _services = services;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<ServiceResponse> Services { get; private set; } = Array.Empty<ServiceResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return;

        ClientConfigured = true;
        Services = await _services.ListAsync(client.Id, activeOnly: false, ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var service = await _services.SetActiveAsync(client.Id, id, active, ct);
        if (service is null) ErrorMessage = "Service not found.";
        else StatusMessage = active
            ? $"{service.Name} is active again."
            : $"{service.Name} deactivated — existing contacts and bookings keep it, but it can't be used for new bookings.";
        return RedirectToPage();
    }
}
