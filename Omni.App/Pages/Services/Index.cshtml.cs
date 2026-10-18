using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Pages.Services;

/// <summary>Services management — the client's structured service catalog (see ICatalogService).
/// The client always comes from the logged-in user (ICurrentClientContext); services are activated /
/// deactivated, never deleted.</summary>
public class IndexModel : PageModel
{
    private const string NotFoundKey = "services.errors.notFound";
    private const string ActivatedKey = "services.activated";
    private const string DeactivatedKey = "services.deactivated";

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
        if (service is null) ErrorMessage = _text[NotFoundKey].Value;
        else StatusMessage = _text[active ? ActivatedKey : DeactivatedKey, service.Name].Value;
        return RedirectToPage();
    }
}
