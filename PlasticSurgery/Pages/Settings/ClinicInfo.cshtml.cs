using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Clinic Info: the general info (hours, location, consultation rules) the AI
/// agent's get_clinic_info tool reads — see Controllers/AiController.cs.</summary>
public class ClinicInfoModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly ApplicationDbContext _db;

    public ClinicInfoModel(ICurrentClinicContext clinicContext, ApplicationDbContext db)
    {
        _clinicContext = clinicContext;
        _db = db;
    }

    public bool ClinicConfigured { get; private set; }

    [BindProperty]
    public string? Phone { get; set; }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Website { get; set; }

    [BindProperty]
    public string? Address { get; set; }

    [BindProperty]
    public string? OperatingHours { get; set; }

    [BindProperty]
    public string? ConsultationInfo { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return Page();
        }

        ClinicConfigured = true;
        Phone = clinic.Phone;
        Email = clinic.Email;
        Website = clinic.Website;
        Address = clinic.Address;
        OperatingHours = clinic.OperatingHours;
        ConsultationInfo = clinic.ConsultationInfo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        clinic.Phone = Phone;
        clinic.Email = Email;
        clinic.Website = Website;
        clinic.Address = Address;
        clinic.OperatingHours = OperatingHours;
        clinic.ConsultationInfo = ConsultationInfo;
        clinic.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        StatusMessage = "Clinic info saved.";
        return RedirectToPage();
    }
}
