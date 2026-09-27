using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Calendar Integrations: connect Google/Outlook calendars for one-way SculptFlow → external
/// appointment sync via n8n. Plain server-rendered forms, no JS — same idiom as Clinic Info's Availability tab.</summary>
public class CalendarIntegrationsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly ICalendarIntegrationService _calendar;

    public CalendarIntegrationsModel(ICurrentClinicContext clinicContext, ICalendarIntegrationService calendar)
    {
        _clinicContext = clinicContext;
        _calendar = calendar;
    }

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<CalendarIntegrationResponse> Integrations { get; private set; } = Array.Empty<CalendarIntegrationResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) { ClinicConfigured = false; return; }

        ClinicConfigured = true;
        Integrations = await _calendar.ListAsync(clinic.Id, ct);
    }

    public async Task<IActionResult> OnPostConnectAsync(string provider, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            await _calendar.RequestConnectAsync(clinic.Id, provider, ct);
            StatusMessage = $"Connecting {ProviderLabel(provider)}… this can take a moment.";
        }
        catch (ArgumentException ex) { ErrorMessage = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshCalendarsAsync(string provider, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            await _calendar.RequestRefreshCalendarsAsync(clinic.Id, provider, ct);
            StatusMessage = "Refreshing the calendar list…";
        }
        catch (ArgumentException ex) { ErrorMessage = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSelectCalendarAsync(string provider, string externalCalendarId, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            await _calendar.SelectCalendarAsync(clinic.Id, provider, externalCalendarId, ct);
            StatusMessage = "Calendar selected.";
        }
        catch (ArgumentException ex) { ErrorMessage = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetSyncEnabledAsync(string provider, bool enabled, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            await _calendar.SetSyncEnabledAsync(clinic.Id, provider, enabled, ct);
            StatusMessage = enabled ? "Sync turned on." : "Sync turned off.";
        }
        catch (ArgumentException ex) { ErrorMessage = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(string provider, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        await _calendar.DisconnectAsync(clinic.Id, provider, ct);
        StatusMessage = $"{ProviderLabel(provider)} disconnected.";
        return RedirectToPage();
    }

    public static string ProviderLabel(string provider) => provider == CalendarProvider.Google ? "Google Calendar" : "Outlook Calendar";
}
