using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Procedures;

/// <summary>Procedures management — the clinic's structured procedure catalog (see IProcedureService).
/// The clinic always comes from the logged-in user (ICurrentClinicContext); procedures are activated /
/// deactivated, never deleted.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IProcedureService _procedures;

    public IndexModel(ICurrentClinicContext clinicContext, IProcedureService procedures)
    {
        _clinicContext = clinicContext;
        _procedures = procedures;
    }

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<ProcedureResponse> Procedures { get; private set; } = Array.Empty<ProcedureResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return;

        ClinicConfigured = true;
        Procedures = await _procedures.ListAsync(clinic.Id, activeOnly: false, ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        var procedure = await _procedures.SetActiveAsync(clinic.Id, id, active, ct);
        if (procedure is null) ErrorMessage = "Procedure not found.";
        else StatusMessage = active
            ? $"{procedure.Name} is active again."
            : $"{procedure.Name} deactivated — existing leads and appointments keep it, but it can't be used for new bookings.";
        return RedirectToPage();
    }
}
