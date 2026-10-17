using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Clients;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Business Info. Tabs: General (the free-text info the AI's get_client_info tool quotes),
/// Availability (the structured weekly schedule, booking rules and date exceptions that drive real booking slots —
/// see Business/Services/Bookings/AvailabilityService.cs) and AI assistant (whether the AI bot answers new and
/// reopened conversations; shown only when the plan includes the AI assistant — see InboxSettingsService). The free-text
/// Operating hours are never parsed for booking.</summary>
public class BusinessInfoModel : PageModel
{
    /// <summary>Common IANA timezones for the picker; the client's current value is always added if missing.</summary>
    public static readonly string[] Timezones =
    {
        "UTC", "Asia/Beirut", "Asia/Dubai", "Asia/Riyadh", "Asia/Kuwait", "Asia/Qatar", "Asia/Baghdad", "Asia/Amman",
        "Asia/Jerusalem", "Asia/Tehran", "Asia/Karachi", "Asia/Kolkata", "Asia/Dhaka", "Asia/Bangkok", "Asia/Jakarta",
        "Asia/Singapore", "Asia/Hong_Kong", "Asia/Shanghai", "Asia/Tokyo", "Asia/Seoul",
        "Africa/Cairo", "Africa/Lagos", "Africa/Nairobi", "Africa/Johannesburg", "Africa/Casablanca",
        "Europe/London", "Europe/Dublin", "Europe/Lisbon", "Europe/Paris", "Europe/Madrid", "Europe/Berlin", "Europe/Rome",
        "Europe/Athens", "Europe/Istanbul", "Europe/Moscow", "Europe/Kyiv",
        "America/St_Johns", "America/Halifax", "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles",
        "America/Mexico_City", "America/Bogota", "America/Sao_Paulo", "America/Argentina/Buenos_Aires",
        "Australia/Perth", "Australia/Adelaide", "Australia/Sydney", "Pacific/Auckland"
    };

    // Weekday display order (Mon first); values are .NET DayOfWeek numbers, matching the stored day_of_week.
    private static readonly int[] DayOrder = { 1, 2, 3, 4, 5, 6, 0 };

    private readonly ICurrentClientContext _clientContext;
    private readonly IClientProfileService _profile;
    private readonly IAvailabilityService _availability;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IStringLocalizer _text;

    private const string DetailsSavedKey = "businessInfo.done.details";
    private const string NoAiKey = "businessInfo.errors.noAi";
    private const string AiSavedKey = "businessInfo.done.ai";
    private const string HoursSavedKey = "businessInfo.done.hours";
    private const string DateSavedKey = "businessInfo.done.dateSaved";
    private const string DateRemovedKey = "businessInfo.done.dateRemoved";

    public BusinessInfoModel(ICurrentClientContext clientContext, IClientProfileService profile, IAvailabilityService availability,
        IInboxSettingsService inboxSettings, IStringLocalizer text)
    {
        _inboxSettings = inboxSettings;
        _text = text;
        _clientContext = clientContext;
        _profile = profile;
        _availability = availability;
    }

    public bool ClientConfigured { get; private set; }

    /// <summary>"general", "availability" or "ai".</summary>
    public string ActiveTab { get; private set; } = "general";

    /// <summary>What the Availability tab renders (saved values, or the just-posted values when validation failed).</summary>
    public AvailabilitySettingsDto? Availability { get; private set; }

    /// <summary>The client's AI bot switches and whether its plan includes the bot (the AI assistant tab shows only then).</summary>
    public InboxSettingsDto? InboxSettings { get; private set; }

    // ---- AI assistant tab inputs (only posted by its own form)
    [BindProperty]
    public bool AiBotNewConversations { get; set; }

    [BindProperty]
    public bool AiBotReopenedConversations { get; set; }

    /// <summary>The client's name (given at registration) — shown in the sidebar and to the AI. The URL slug is NOT
    /// changed when the name is edited.</summary>
    [BindProperty]
    public string Name { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

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
    public string? BookingInfo { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    // ---- Availability tab inputs (only posted by its own forms)
    [BindProperty]
    public List<AvailabilityDayInput> Days { get; set; } = new();

    [BindProperty]
    public string? TimezoneId { get; set; }

    [BindProperty]
    public int DefaultDurationMinutes { get; set; } = 30;

    [BindProperty]
    public int BufferMinutes { get; set; }

    [BindProperty]
    public int NoticeHours { get; set; } = 4;

    [BindProperty]
    public int MaxAdvanceDays { get; set; } = 60;

    [BindProperty]
    public string? ExceptionDate { get; set; }

    [BindProperty]
    public bool ExceptionClosed { get; set; }

    [BindProperty]
    public string? ExceptionStart { get; set; }

    [BindProperty]
    public string? ExceptionEnd { get; set; }

    [BindProperty]
    public string? ExceptionReason { get; set; }

    public async Task<IActionResult> OnGetAsync(string? tab, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return Page();
        }

        ClientConfigured = true;
        Name = client.Name;
        Phone = client.Phone;
        Email = client.Email;
        Website = client.Website;
        Address = client.Address;
        OperatingHours = client.OperatingHours;
        BookingInfo = client.BookingInfo;
        InboxSettings = await _inboxSettings.GetAsync(client.Id, ct);

        if (string.Equals(tab, "availability", StringComparison.OrdinalIgnoreCase))
        {
            ActiveTab = "availability";
            Availability = await _availability.GetSettingsAsync(client.Id, ct);
        }
        else if (string.Equals(tab, "ai", StringComparison.OrdinalIgnoreCase) && InboxSettings.PlanIncludesAiBot)
        {
            ActiveTab = "ai";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            await _profile.UpdateDetailsAsync(client.Id,
                new UpdateClientDetailsRequest(Name, Phone, Email, Website, Address, OperatingHours, BookingInfo), ct);
        }
        catch (ArgumentException ex)
        {
            // Redisplay the form with what was typed instead of saving.
            ClientConfigured = true;
            Name = (Name ?? string.Empty).Trim();
            ErrorMessage = _text.Localize(ex);
            InboxSettings = await _inboxSettings.GetAsync(client.Id, ct);
            return Page();
        }

        StatusMessage = _text[DetailsSavedKey].Value;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAiBotAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        // The switches are only offered while the plan includes the AI assistant; a stale form posted after a downgrade
        // changes nothing.
        if (!(await _inboxSettings.GetAsync(client.Id, ct)).PlanIncludesAiBot)
        {
            StatusMessage = _text[NoAiKey].Value;
            return RedirectToPage();
        }

        await _inboxSettings.UpdateAsync(client.Id, new UpdateInboxSettingsRequest(AiBotNewConversations, AiBotReopenedConversations), ct);
        StatusMessage = _text[AiSavedKey].Value;
        return RedirectToPage(new { tab = "ai" });
    }

    public async Task<IActionResult> OnPostSaveAvailabilityAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var days = Days.Select(d => new DayRuleDto(d.DayOfWeek, d.IsOpen, d.Start ?? string.Empty, d.End ?? string.Empty)).ToList();
        var booking = new BookingRulesDto(DefaultDurationMinutes, BufferMinutes, NoticeHours * 60, MaxAdvanceDays);
        var timezone = TimezoneId ?? string.Empty;

        try
        {
            await _availability.SaveScheduleAsync(client.Id, timezone, days, booking, ct);
        }
        catch (ArgumentException ex)
        {
            ClientConfigured = true;
            ActiveTab = "availability";
            ErrorMessage = _text.Localize(ex);
            var saved = await _availability.GetSettingsAsync(client.Id, ct);
            Availability = saved with { Timezone = timezone, Days = days, Booking = booking };
            await FillGeneralAsync(client);
            return Page();
        }

        StatusMessage = _text[HoursSavedKey].Value;
        return RedirectToPage(new { tab = "availability" });
    }

    public async Task<IActionResult> OnPostAddExceptionAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            if (!DateOnly.TryParse(ExceptionDate, out var date)) throw new ArgumentException("businessInfo.errors.pickDate");
            await _availability.SaveExceptionAsync(client.Id, date, ExceptionClosed, ExceptionStart, ExceptionEnd, ExceptionReason, ct);
        }
        catch (ArgumentException ex)
        {
            ClientConfigured = true;
            ActiveTab = "availability";
            ErrorMessage = _text.Localize(ex);
            Availability = await _availability.GetSettingsAsync(client.Id, ct);
            await FillGeneralAsync(client);
            return Page();
        }

        StatusMessage = _text[DateSavedKey].Value;
        return RedirectToPage(new { tab = "availability" });
    }

    public async Task<IActionResult> OnPostDeleteExceptionAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        await _availability.DeleteExceptionAsync(client.Id, id, ct);
        StatusMessage = _text[DateRemovedKey].Value;
        return RedirectToPage(new { tab = "availability" });
    }

    /// <summary>Weekday rows in Mon..Sun display order.</summary>
    public IEnumerable<DayRuleDto> OrderedDays() =>
        DayOrder.Select(dow => Availability!.Days.First(d => d.DayOfWeek == dow));

    public IEnumerable<string> TimezoneOptions(string current) =>
        Timezones.Contains(current) ? Timezones : Timezones.Prepend(current);

    private async Task FillGeneralAsync(Client client)
    {
        InboxSettings = await _inboxSettings.GetAsync(client.Id);
        Name = client.Name;
        Phone = client.Phone;
        Email = client.Email;
        Website = client.Website;
        Address = client.Address;
        OperatingHours = client.OperatingHours;
        BookingInfo = client.BookingInfo;
    }
}
