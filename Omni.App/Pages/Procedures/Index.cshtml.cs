using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Procedures;
using Omni.App.Entities.Responses.Procedures;

namespace Omni.App.Pages.Procedures;

/// <summary>Procedures management — the client's structured procedure catalog (see IProcedureService).
/// The client always comes from the logged-in user (ICurrentClientContext); procedures are activated /
/// deactivated, never deleted.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IProcedureService _procedures;

    public IndexModel(ICurrentClientContext clientContext, IProcedureService procedures)
    {
        _clientContext = clientContext;
        _procedures = procedures;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<ProcedureResponse> Procedures { get; private set; } = Array.Empty<ProcedureResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return;

        ClientConfigured = true;
        Procedures = await _procedures.ListAsync(client.Id, activeOnly: false, ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var procedure = await _procedures.SetActiveAsync(client.Id, id, active, ct);
        if (procedure is null) ErrorMessage = "Procedure not found.";
        else StatusMessage = active
            ? $"{procedure.Name} is active again."
            : $"{procedure.Name} deactivated — existing leads and appointments keep it, but it can't be used for new bookings.";
        return RedirectToPage();
    }
}
