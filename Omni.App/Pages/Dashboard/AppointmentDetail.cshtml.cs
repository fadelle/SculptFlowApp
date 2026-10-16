using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Appointments;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Pages.Dashboard;

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
    private readonly ICurrentClientContext _clientContext;
    private readonly IAppointmentService _appointments;

    public AppointmentDetailModel(ICurrentClientContext clientContext, IAppointmentService appointments)
    {
        _clientContext = clientContext;
        _appointments = appointments;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClientConfigured { get; private set; }
    public AppointmentResponse? Appointment { get; private set; }

    /// <summary>For the "Client time | My time" switch (_TimeViewSwitch); the header time defaults to client time.</summary>
    public string ClientTimezone { get; private set; } = "UTC";
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
        if (ClientConfigured && Appointment is null) return NotFound();
        if (Appointment is not null) Status = Appointment.Status;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            var appointment = await _appointments.UpdateStatusAsync(client.Id, Id, Status, ct);
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
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        if (!string.IsNullOrWhiteSpace(client.Timezone)) ClientTimezone = client.Timezone;
        Appointment = await _appointments.GetByIdAsync(client.Id, Id, ct);
    }
}
