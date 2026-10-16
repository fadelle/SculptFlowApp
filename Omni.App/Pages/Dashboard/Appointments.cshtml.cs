using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Pages.Dashboard;

/// <summary>Page 3 — Appointments: a month calendar. Only the page shell; everything on it (the visible month's
/// appointments, the day drawer, creating an appointment) is loaded and driven through the existing
/// /api/appointments API (wwwroot/js/appointments-calendar.js) — the SAME appointments table and services used
/// by the AI agent's booking tools and the rest of the dashboard. Clicking an appointment in the day drawer opens
/// the existing /dashboard/appointments/{id} detail page (AppointmentDetailModel) — reused as-is, not duplicated.</summary>
public class AppointmentsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;

    public AppointmentsModel(ICurrentClientContext clientContext)
    {
        _clientContext = clientContext;
    }

    public bool ClientConfigured { get; private set; }

    /// <summary>For the "Client time | My time" switch (_TimeViewSwitch).</summary>
    public string ClientTimezone { get; private set; } = "UTC";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        ClientConfigured = client is not null;
        if (client is not null && !string.IsNullOrWhiteSpace(client.Timezone)) ClientTimezone = client.Timezone;
    }
}
