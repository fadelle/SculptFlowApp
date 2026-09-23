using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Clinic Info. Two tabs: General (the free-text info the AI's get_clinic_info tool quotes) and
/// Availability (the structured weekly schedule, booking rules and date exceptions that drive real appointment slots —
/// see Services/AvailabilityService.cs). The free-text Operating hours are never parsed for booking.</summary>
public class ClinicInfoModel : PageModel
{
    /// <summary>Common IANA timezones for the picker; the clinic's current value is always added if missing.</summary>
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

    private readonly ICurrentClinicContext _clinicContext;
    private readonly ApplicationDbContext _db;
    private readonly IAvailabilityService _availability;

    public ClinicInfoModel(ICurrentClinicContext clinicContext, ApplicationDbContext db, IAvailabilityService availability)
    {
        _clinicContext = clinicContext;
        _db = db;
        _availability = availability;
    }

    public bool ClinicConfigured { get; private set; }

    /// <summary>"general" or "availability".</summary>
    public string ActiveTab { get; private set; } = "general";

    /// <summary>What the Availability tab renders (saved values, or the just-posted values when validation failed).</summary>
    public AvailabilitySettingsDto? Availability { get; private set; }

    /// <summary>The clinic's name (given at registration) — shown in the sidebar and to the AI. The URL slug is NOT
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
    public string? ConsultationInfo { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    // ---- Availability tab inputs (only posted by its own forms)
    public class DayInput
    {
        public int DayOfWeek { get; set; }
        public bool IsOpen { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
    }

    [BindProperty]
    public List<DayInput> Days { get; set; } = new();

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
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return Page();
        }

        ClinicConfigured = true;
        Name = clinic.Name;
        Phone = clinic.Phone;
        Email = clinic.Email;
        Website = clinic.Website;
        Address = clinic.Address;
        OperatingHours = clinic.OperatingHours;
        ConsultationInfo = clinic.ConsultationInfo;

        if (string.Equals(tab, "availability", StringComparison.OrdinalIgnoreCase))
        {
            ActiveTab = "availability";
            Availability = await _availability.GetSettingsAsync(clinic.Id, ct);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        var name = (Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 200)
        {
            // Redisplay the form with what was typed instead of saving.
            ClinicConfigured = true;
            Name = name;
            ErrorMessage = name.Length == 0 ? "Clinic name is required." : "Clinic name must be 200 characters or fewer.";
            return Page();
        }

        clinic.Name = name;
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

    public async Task<IActionResult> OnPostSaveAvailabilityAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        var days = Days.Select(d => new DayRuleDto(d.DayOfWeek, d.IsOpen, d.Start ?? string.Empty, d.End ?? string.Empty)).ToList();
        var booking = new BookingRulesDto(DefaultDurationMinutes, BufferMinutes, NoticeHours * 60, MaxAdvanceDays);
        var timezone = TimezoneId ?? string.Empty;

        try
        {
            await _availability.SaveScheduleAsync(clinic.Id, timezone, days, booking, ct);
        }
        catch (ArgumentException ex)
        {
            ClinicConfigured = true;
            ActiveTab = "availability";
            ErrorMessage = ex.Message;
            var saved = await _availability.GetSettingsAsync(clinic.Id, ct);
            Availability = saved with { Timezone = timezone, Days = days, Booking = booking };
            await FillGeneralAsync(clinic);
            return Page();
        }

        StatusMessage = "Availability saved.";
        return RedirectToPage(new { tab = "availability" });
    }

    public async Task<IActionResult> OnPostAddExceptionAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            if (!DateOnly.TryParse(ExceptionDate, out var date)) throw new ArgumentException("Pick a date for the exception.");
            await _availability.SaveExceptionAsync(clinic.Id, date, ExceptionClosed, ExceptionStart, ExceptionEnd, ExceptionReason, ct);
        }
        catch (ArgumentException ex)
        {
            ClinicConfigured = true;
            ActiveTab = "availability";
            ErrorMessage = ex.Message;
            Availability = await _availability.GetSettingsAsync(clinic.Id, ct);
            await FillGeneralAsync(clinic);
            return Page();
        }

        StatusMessage = "Exception saved.";
        return RedirectToPage(new { tab = "availability" });
    }

    public async Task<IActionResult> OnPostDeleteExceptionAsync(Guid id, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        await _availability.DeleteExceptionAsync(clinic.Id, id, ct);
        StatusMessage = "Exception removed.";
        return RedirectToPage(new { tab = "availability" });
    }

    /// <summary>Weekday rows in Mon..Sun display order.</summary>
    public IEnumerable<DayRuleDto> OrderedDays() =>
        DayOrder.Select(dow => Availability!.Days.First(d => d.DayOfWeek == dow));

    public IEnumerable<string> TimezoneOptions(string current) =>
        Timezones.Contains(current) ? Timezones : Timezones.Prepend(current);

    private Task FillGeneralAsync(Data.Entities.Clinic clinic)
    {
        Name = clinic.Name;
        Phone = clinic.Phone;
        Email = clinic.Email;
        Website = clinic.Website;
        Address = clinic.Address;
        OperatingHours = clinic.OperatingHours;
        ConsultationInfo = clinic.ConsultationInfo;
        return Task.CompletedTask;
    }
}
