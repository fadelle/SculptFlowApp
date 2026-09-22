using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>Page 3 — Appointments: a month calendar. Only the page shell; everything on it (the visible month's
/// appointments, the day drawer, creating an appointment) is loaded and driven through the existing
/// /api/appointments API (wwwroot/js/appointments-calendar.js) — the SAME appointments table and services used
/// by the AI agent's booking tools and the rest of the dashboard. Clicking an appointment in the day drawer opens
/// the existing /dashboard/appointments/{id} detail page (AppointmentDetailModel) — reused as-is, not duplicated.</summary>
public class AppointmentsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;

    public AppointmentsModel(ICurrentClinicContext clinicContext)
    {
        _clinicContext = clinicContext;
    }

    public bool ClinicConfigured { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ClinicConfigured = await _clinicContext.GetClinicAsync(ct) is not null;
    }
}
