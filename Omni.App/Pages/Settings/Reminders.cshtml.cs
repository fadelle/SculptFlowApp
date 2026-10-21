using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Requests.Bookings;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Booking reminders: on/off, hours before each of the two reminders, and the approved WhatsApp
/// template per dashboard language (IBookingReminderService). Any member may change them; the page says so when the plan
/// lacks bookings.</summary>
public class RemindersModel : PageModel
{
    private const string SavedKey = "reminders.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly IBookingReminderService _reminders;
    private readonly IEntitlementService _entitlements;
    private readonly ITextContext _textContext;
    private readonly IStringLocalizer _text;

    public RemindersModel(ICurrentClientContext clientContext, IBookingReminderService reminders, IEntitlementService entitlements,
        ITextContext textContext, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _reminders = reminders;
        _entitlements = entitlements;
        _textContext = textContext;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public bool BookingsIncluded { get; private set; } = true;

    /// <summary>Approved WhatsApp templates the client can pick.</summary>
    public IReadOnlyList<ReminderTemplateOption> TemplateOptions { get; private set; } = [];

    /// <summary>The dashboard languages offered (a template per language).</summary>
    public IReadOnlyList<string> Languages { get; private set; } = [];

    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public int FirstHoursBefore { get; set; } = 24;
    [BindProperty] public int SecondHoursBefore { get; set; } = 2;
    [BindProperty] public Dictionary<string, Guid?> Templates { get; set; } = new();

    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clientId = await LoadAsync(ct);
        if (clientId is null) return;
        var settings = await _reminders.GetSettingsAsync(clientId.Value, ct);
        Enabled = settings.Enabled;
        FirstHoursBefore = settings.FirstHoursBefore;
        SecondHoursBefore = settings.SecondHoursBefore;
        Templates = settings.Templates.ToDictionary(t => t.Key, t => (Guid?)t.Value);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clientId = await LoadAsync(ct);
        if (clientId is null) return RedirectToPage();
        try
        {
            await _reminders.SaveSettingsAsync(clientId.Value, new SaveReminderSettingsRequest(Enabled, FirstHoursBefore, SecondHoursBefore, Templates), ct);
            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            return Page();
        }
    }

    private async Task<Guid?> LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return null;
        ClientConfigured = true;
        BookingsIncluded = (await _entitlements.GetAsync(client.Id, ct)).CanUseBookings;
        Languages = _textContext.Offered;
        TemplateOptions = await _reminders.ListTemplateOptionsAsync(client.Id, ct);
        return client.Id;
    }
}
