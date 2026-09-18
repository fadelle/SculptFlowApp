using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>
/// Staff-facing edit page for Appointment Status — posts through the same
/// IAppointmentService.UpdateStatusAsync the existing PATCH /api/appointments/{id}/status endpoint
/// already uses, so nothing here duplicates that logic. Matters most for "Old leads who never
/// booked": marking an appointment Booked/Confirmed/Attended here makes the lead stop matching the
/// reactivation audience on the very next count/preview, since ICampaignAudienceService always reads
/// live appointment data — see its remarks.
/// </summary>
public class AppointmentDetailModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IAppointmentService _appointments;

    public AppointmentDetailModel(ICurrentClinicContext clinicContext, IAppointmentService appointments)
    {
        _clinicContext = clinicContext;
        _appointments = appointments;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClinicConfigured { get; private set; }
    public AppointmentResponse? Appointment { get; private set; }
    public IReadOnlyList<string> StatusOptions { get; } = AppointmentStatus.All.OrderBy(s => s).ToList();

    [BindProperty]
    public string Status { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (ClinicConfigured && Appointment is null) return NotFound();
        if (Appointment is not null) Status = Appointment.Status;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            var appointment = await _appointments.UpdateStatusAsync(clinic.Id, Id, Status, ct);
            if (appointment is null) return NotFound();

            StatusMessage = "Saved.";
            return RedirectToPage(new { Id });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        Appointment = await _appointments.GetByIdAsync(clinic.Id, Id, ct);
    }
}
