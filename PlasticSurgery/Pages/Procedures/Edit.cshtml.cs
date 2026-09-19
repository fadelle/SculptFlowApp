using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Procedures;

/// <summary>Add / edit one procedure (name, code, consultation duration, short description). New
/// procedures start active; activating/deactivating happens from the list. The clinic always comes from
/// the logged-in user.</summary>
public class EditModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IProcedureService _procedures;

    public EditModel(ICurrentClinicContext clinicContext, IProcedureService procedures)
    {
        _clinicContext = clinicContext;
        _procedures = procedures;
    }

    /// <summary>Route value — null when adding a new procedure.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    public bool ClinicConfigured { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string? Code { get; set; }

    [BindProperty]
    public int? DurationMinutes { get; set; }

    [BindProperty]
    public string? Description { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return Page();
        ClinicConfigured = true;

        if (Id is null) return Page();

        var procedure = await _procedures.GetByIdAsync(clinic.Id, Id.Value, ct);
        if (procedure is null) return NotFound();

        Name = procedure.Name;
        Code = procedure.Code;
        DurationMinutes = procedure.ConsultationDuration;
        Description = procedure.Description;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/Procedures/Index");
        ClinicConfigured = true;

        try
        {
            if (Id is null)
            {
                await _procedures.CreateAsync(new CreateProcedureRequest(clinic.Id, Name, Code, Description, DurationMinutes), ct);
            }
            else if (await _procedures.UpdateAsync(clinic.Id, Id.Value, new UpdateProcedureRequest(Name, Code, Description, DurationMinutes), ct) is null)
            {
                return NotFound();
            }

            TempData["StatusMessage"] = "Saved.";
            return RedirectToPage("/Procedures/Index");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
