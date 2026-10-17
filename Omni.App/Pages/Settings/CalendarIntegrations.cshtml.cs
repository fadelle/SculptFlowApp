using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Responses.Calendars;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Calendar Integrations: connect Google/Outlook calendars for one-way Omni → external
/// booking sync. Plain server-rendered forms, no JS — same idiom as Business Info's Availability tab — except
/// "Connect", which is a plain link to CalendarOAuthController (a full-page redirect to the provider's own consent
/// screen can't happen from a form POST handler). That controller sets StatusMessage/ErrorMessage TempData directly
/// before redirecting back here, the same TempData keys OnPost* below use.</summary>
public class CalendarIntegrationsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private const string RefreshedKey = "calendars.done.refreshed";
    private const string SelectedKey = "calendars.done.selected";
    private const string SyncOnKey = "calendars.done.syncOn";
    private const string SyncOffKey = "calendars.done.syncOff";
    private const string DisconnectedKey = "calendars.done.disconnected";

    private readonly ICalendarIntegrationService _calendar;
    private readonly IStringLocalizer _text;

    public CalendarIntegrationsModel(ICurrentClientContext clientContext, ICalendarIntegrationService calendar, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _calendar = calendar;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<CalendarIntegrationResponse> Integrations { get; private set; } = Array.Empty<CalendarIntegrationResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) { ClientConfigured = false; return; }

        ClientConfigured = true;
        Integrations = await _calendar.ListAsync(client.Id, ct);
    }

    public async Task<IActionResult> OnPostRefreshCalendarsAsync(string provider, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            await _calendar.RequestRefreshCalendarsAsync(client.Id, provider, ct);
            StatusMessage = _text[RefreshedKey].Value;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ErrorMessage = _text.Localize(ex); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSelectCalendarAsync(string provider, string externalCalendarId, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            await _calendar.SelectCalendarAsync(client.Id, provider, externalCalendarId, ct);
            StatusMessage = _text[SelectedKey].Value;
        }
        catch (ArgumentException ex) { ErrorMessage = _text.Localize(ex); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetSyncEnabledAsync(string provider, bool enabled, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            await _calendar.SetSyncEnabledAsync(client.Id, provider, enabled, ct);
            StatusMessage = _text[enabled ? SyncOnKey : SyncOffKey].Value;
        }
        catch (ArgumentException ex) { ErrorMessage = _text.Localize(ex); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(string provider, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        await _calendar.DisconnectAsync(client.Id, provider, ct);
        StatusMessage = _text[DisconnectedKey, ProviderLabel(provider)].Value;
        return RedirectToPage();
    }

    public static string ProviderLabel(string provider) => provider == CalendarProvider.Google ? "Google Calendar" : "Outlook Calendar";
}
