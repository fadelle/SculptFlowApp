using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class AvailabilityService : IAvailabilityService
{
    private const int DefaultDurationMinutes = 30;
    private const int DefaultBufferMinutes = 0;
    private const int DefaultNoticeMinutes = 240;
    private const int DefaultHorizonDays = 60;
    private const int MaxRangeDays = 14;

    private readonly ApplicationDbContext _db;

    public AvailabilityService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------ slots

    public async Task<AvailabilityResponse> GetSlotsAsync(
        Guid clinicId, Guid? procedureId, DateOnly? from, int days, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clinicId, ct);
        if (ctx is null) return NotConfigured("UTC", DefaultDurationMinutes, DateOnly.FromDateTime(DateTime.UtcNow), "Clinic not found.");

        var duration = await ResolveDurationAsync(clinicId, procedureId, ctx.Booking.DefaultConsultationDurationMinutes, ct);
        var nowUtc = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, ctx.Tz).DateTime);

        if (!ctx.Configured)
        {
            return NotConfigured(ctx.Tz.Id, duration, today,
                "This clinic hasn't set up its appointment availability yet, so no times can be offered.");
        }

        // A past date is almost always the caller working from the wrong "today". Don't silently search other dates —
        // say so, so the AI can correct itself using Today/Tomorrow in this response.
        if (from is { } requested && requested < today)
        {
            return new AvailabilityResponse(true, ctx.Tz.Id, today.ToString("yyyy-MM-dd"), duration,
                requested.ToString("yyyy-MM-dd"), requested.ToString("yyyy-MM-dd"),
                $"The requested date {requested:yyyy-MM-dd} is in the past. Today is {today:yyyy-MM-dd} ({today.DayOfWeek}). " +
                "Resolve the patient's day from today's date and call again with a date that is today or later.",
                Array.Empty<AvailableSlotResponse>());
        }

        days = Math.Clamp(days, 1, MaxRangeDays);
        var start = from ?? today;
        var maxDate = today.AddDays(ctx.Booking.MaximumAdvanceBookingDays);
        var end = start.AddDays(days - 1);
        if (end > maxDate) end = maxDate;

        if (start > maxDate)
        {
            return new AvailabilityResponse(true, ctx.Tz.Id, today.ToString("yyyy-MM-dd"), duration, start.ToString("yyyy-MM-dd"), start.ToString("yyyy-MM-dd"),
                $"Bookings can only be made up to {ctx.Booking.MaximumAdvanceBookingDays} days ahead.", Array.Empty<AvailableSlotResponse>());
        }

        await ctx.LoadExceptionsAsync(_db, clinicId, start, end, ct);
        var busy = await LoadBusyAsync(clinicId, ctx, start, end, ct);

        var buffer = ctx.Booking.BufferMinutes;
        var step = duration + buffer;
        var earliest = nowUtc.AddMinutes(ctx.Booking.MinimumBookingNoticeMinutes);
        var slots = new List<AvailableSlotResponse>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            foreach (var (windowStart, windowEnd) in ctx.Windows(date))
            {
                var cursor = date.ToDateTime(windowStart);
                var limit = date.ToDateTime(windowEnd);

                // Next-fit: take a slot, advance by duration + buffer; when an existing appointment (plus buffer) is in the way,
                // jump to the first moment after it. Keeps slots on a tidy grid while still using the gaps around off-grid bookings.
                while (cursor.AddMinutes(duration) <= limit)
                {
                    if (ctx.Tz.IsInvalidTime(cursor)) { cursor = cursor.AddMinutes(step); continue; }

                    var slotStart = new DateTimeOffset(cursor, ctx.Tz.GetUtcOffset(cursor));
                    var slotEnd = slotStart.AddMinutes(duration);

                    if (slotStart < earliest) { cursor = cursor.AddMinutes(step); continue; }

                    if (IsBlocked(busy, buffer, slotStart, slotEnd, out var freeFrom))
                    {
                        var freeLocal = TimeZoneInfo.ConvertTime(freeFrom, ctx.Tz).DateTime;
                        cursor = freeLocal > cursor ? freeLocal : cursor.AddMinutes(step);
                        continue;
                    }

                    slots.Add(new AvailableSlotResponse(slotStart, slotEnd,
                        date.ToString("yyyy-MM-dd"), cursor.ToString("HH:mm"),
                        $"{date.DayOfWeek} {cursor.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}"));
                    cursor = cursor.AddMinutes(step);
                }
            }
        }

        var message = slots.Count == 0 ? "No available times in this date range." : null;
        return new AvailabilityResponse(true, ctx.Tz.Id, today.ToString("yyyy-MM-dd"), duration, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), message, slots);
    }

    public async Task<SlotCheck> CheckSlotAsync(
        Guid clinicId, Guid? procedureId, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clinicId, ct);
        var fallbackEnd = end ?? start.AddMinutes(DefaultDurationMinutes);
        if (ctx is null) return new SlotCheck("Clinic not found.", fallbackEnd);
        if (!ctx.Configured) return new SlotCheck("This clinic hasn't set up its appointment availability yet.", fallbackEnd);

        var duration = await ResolveDurationAsync(clinicId, procedureId, ctx.Booking.DefaultConsultationDurationMinutes, ct);
        var slotEnd = end ?? start.AddMinutes(duration);
        if (slotEnd <= start) return new SlotCheck("The appointment must end after it starts.", slotEnd);

        var nowUtc = DateTimeOffset.UtcNow;
        if (start < nowUtc.AddMinutes(ctx.Booking.MinimumBookingNoticeMinutes))
        {
            return new SlotCheck("That time is too soon - it is inside the clinic's minimum booking notice.", slotEnd);
        }

        var startLocal = TimeZoneInfo.ConvertTime(start, ctx.Tz).DateTime;
        var endLocal = TimeZoneInfo.ConvertTime(slotEnd, ctx.Tz).DateTime;
        var date = DateOnly.FromDateTime(startLocal);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, ctx.Tz).DateTime);
        if (date > today.AddDays(ctx.Booking.MaximumAdvanceBookingDays))
        {
            return new SlotCheck($"Bookings can only be made up to {ctx.Booking.MaximumAdvanceBookingDays} days ahead.", slotEnd);
        }

        await ctx.LoadExceptionsAsync(_db, clinicId, date, date, ct);
        var inWindow = ctx.Windows(date).Any(w =>
            DateOnly.FromDateTime(endLocal) == date
            && TimeOnly.FromDateTime(startLocal) >= w.Start && TimeOnly.FromDateTime(endLocal) <= w.End);
        if (!inWindow) return new SlotCheck("The clinic is not open for a consultation at that time.", slotEnd);

        var busy = await LoadBusyAsync(clinicId, ctx, date, date, ct);
        if (IsBlocked(busy, ctx.Booking.BufferMinutes, start, slotEnd, out _))
        {
            return new SlotCheck("That time is no longer available - it overlaps another appointment.", slotEnd);
        }

        return new SlotCheck(null, slotEnd);
    }

    // ------------------------------------------------------------------ settings (Clinic Info → Availability)

    public async Task<AvailabilitySettingsDto> GetSettingsAsync(Guid clinicId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clinicId, ct)
                  ?? throw new ArgumentException("Clinic not found.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ctx.Tz).DateTime);

        var days = Enumerable.Range(0, 7).Select(dow =>
        {
            var rule = ctx.AllRules.FirstOrDefault(r => r.DayOfWeek == dow);
            return rule is null
                ? new DayRuleDto(dow, false, "09:00", "17:00")
                : new DayRuleDto(dow, rule.IsOpen, Fmt(rule.StartTime), Fmt(rule.EndTime));
        }).ToList();

        var exceptions = await _db.ClinicAvailabilityExceptions
            .Where(e => e.ClinicId == clinicId && e.Date >= today)
            .OrderBy(e => e.Date)
            .ToListAsync(ct);

        return new AvailabilitySettingsDto(
            ctx.Tz.Id,
            ctx.Configured,
            days,
            new BookingRulesDto(ctx.Booking.DefaultConsultationDurationMinutes, ctx.Booking.BufferMinutes,
                ctx.Booking.MinimumBookingNoticeMinutes, ctx.Booking.MaximumAdvanceBookingDays),
            exceptions.Select(e => new AvailabilityExceptionDto(
                e.Id, e.Date.ToString("yyyy-MM-dd"), e.IsClosed,
                e.StartTime is null ? null : Fmt(e.StartTime.Value), e.EndTime is null ? null : Fmt(e.EndTime.Value), e.Reason)).ToList());
    }

    public async Task SaveScheduleAsync(
        Guid clinicId, string timezone, IReadOnlyList<DayRuleDto> days, BookingRulesDto booking, CancellationToken ct = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct)
                     ?? throw new ArgumentException("Clinic not found.");

        timezone = (timezone ?? string.Empty).Trim();
        try { TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { throw new ArgumentException("Unknown timezone."); }

        if (booking.DefaultDurationMinutes is < 5 or > 480) throw new ArgumentException("Default consultation duration must be between 5 and 480 minutes.");
        if (booking.BufferMinutes is < 0 or > 240) throw new ArgumentException("Buffer must be between 0 and 240 minutes.");
        if (booking.MinimumNoticeMinutes is < 0 or > 60 * 24 * 30) throw new ArgumentException("Minimum notice must be between 0 and 720 hours.");
        if (booking.MaxAdvanceDays is < 1 or > 365) throw new ArgumentException("Maximum advance booking must be between 1 and 365 days.");

        var parsed = new List<(int Dow, bool Open, TimeOnly Start, TimeOnly End)>();
        foreach (var d in days)
        {
            if (d.DayOfWeek is < 0 or > 6) throw new ArgumentException("Invalid day of week.");
            var okStart = TimeOnly.TryParse(d.Start, out var s);
            var okEnd = TimeOnly.TryParse(d.End, out var e);
            if (d.IsOpen)
            {
                var name = ((DayOfWeek)d.DayOfWeek).ToString();
                if (!okStart || !okEnd) throw new ArgumentException($"{name}: enter a start and end time.");
                if (e <= s) throw new ArgumentException($"{name}: the end time must be after the start time.");
            }
            else if (!okStart || !okEnd || e <= s)
            {
                s = new TimeOnly(9, 0); e = new TimeOnly(17, 0); // a closed day still stores a valid window
            }
            parsed.Add((d.DayOfWeek, d.IsOpen, s, e));
        }
        if (parsed.Select(p => p.Dow).Distinct().Count() != parsed.Count) throw new ArgumentException("Duplicate day in the weekly schedule.");

        var now = DateTimeOffset.UtcNow;
        var rules = await _db.ClinicAvailabilityRules.Where(r => r.ClinicId == clinicId).ToListAsync(ct);
        foreach (var p in parsed)
        {
            var rule = rules.FirstOrDefault(r => r.DayOfWeek == p.Dow);
            if (rule is null)
            {
                _db.ClinicAvailabilityRules.Add(new ClinicAvailabilityRule
                {
                    Id = Guid.NewGuid(), ClinicId = clinicId, DayOfWeek = p.Dow, IsOpen = p.Open,
                    StartTime = p.Start, EndTime = p.End, CreatedAt = now, UpdatedAt = now
                });
            }
            else
            {
                rule.IsOpen = p.Open; rule.StartTime = p.Start; rule.EndTime = p.End; rule.UpdatedAt = now;
            }
        }

        var settings = await _db.ClinicBookingSettings.FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);
        if (settings is null)
        {
            settings = new ClinicBookingSettings { Id = Guid.NewGuid(), ClinicId = clinicId, CreatedAt = now };
            _db.ClinicBookingSettings.Add(settings);
        }
        settings.DefaultConsultationDurationMinutes = booking.DefaultDurationMinutes;
        settings.BufferMinutes = booking.BufferMinutes;
        settings.MinimumBookingNoticeMinutes = booking.MinimumNoticeMinutes;
        settings.MaximumAdvanceBookingDays = booking.MaxAdvanceDays;
        settings.UpdatedAt = now;

        clinic.Timezone = timezone;
        clinic.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveExceptionAsync(
        Guid clinicId, DateOnly date, bool isClosed, string? start, string? end, string? reason, CancellationToken ct = default)
    {
        TimeOnly? s = null, e = null;
        if (!isClosed)
        {
            if (!TimeOnly.TryParse(start, out var ps) || !TimeOnly.TryParse(end, out var pe))
                throw new ArgumentException("Enter a start and end time, or mark the day closed.");
            if (pe <= ps) throw new ArgumentException("The end time must be after the start time.");
            s = ps; e = pe;
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > 200 }) throw new ArgumentException("The reason must be 200 characters or fewer.");

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.ClinicAvailabilityExceptions.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Date == date, ct);
        if (existing is null)
        {
            existing = new ClinicAvailabilityException { Id = Guid.NewGuid(), ClinicId = clinicId, Date = date, CreatedAt = now };
            _db.ClinicAvailabilityExceptions.Add(existing);
        }
        existing.IsClosed = isClosed;
        existing.StartTime = s;
        existing.EndTime = e;
        existing.Reason = reason;
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteExceptionAsync(Guid clinicId, Guid exceptionId, CancellationToken ct = default)
    {
        var existing = await _db.ClinicAvailabilityExceptions.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == exceptionId, ct);
        if (existing is null) return;
        _db.ClinicAvailabilityExceptions.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private static string Fmt(TimeOnly t) => t.ToString("HH:mm");

    private static AvailabilityResponse NotConfigured(string tz, int duration, DateOnly today, string message) =>
        new(false, tz, today.ToString("yyyy-MM-dd"), duration, string.Empty, string.Empty, message, Array.Empty<AvailableSlotResponse>());

    /// <summary>Procedure's own consultation duration when it is active and has one; otherwise the clinic default.
    /// A procedure that is unknown for this clinic or inactive is rejected — it can't be newly booked.</summary>
    private async Task<int> ResolveDurationAsync(Guid clinicId, Guid? procedureId, int clinicDefault, CancellationToken ct)
    {
        if (procedureId is null) return clinicDefault;

        var procedure = await _db.Procedures
            .Where(p => p.Id == procedureId && p.ClinicId == clinicId)
            .Select(p => new { p.Name, p.IsActive, p.ConsultationDuration })
            .FirstOrDefaultAsync(ct);

        if (procedure is null) throw new ArgumentException("Procedure not found for this clinic.");
        if (!procedure.IsActive) throw new ArgumentException($"Procedure '{procedure.Name}' is inactive and can't be newly booked.");

        return procedure.ConsultationDuration is > 0 ? procedure.ConsultationDuration.Value : clinicDefault;
    }

    private async Task<Ctx?> LoadAsync(Guid clinicId, CancellationToken ct)
    {
        var clinic = await _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return null;

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(clinic.Timezone); }
        catch { tz = TimeZoneInfo.Utc; }

        var rules = await _db.ClinicAvailabilityRules.AsNoTracking().Where(r => r.ClinicId == clinicId).ToListAsync(ct);
        var settings = await _db.ClinicBookingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

        return new Ctx(tz, rules, settings ?? new ClinicBookingSettings
        {
            DefaultConsultationDurationMinutes = DefaultDurationMinutes,
            BufferMinutes = DefaultBufferMinutes,
            MinimumBookingNoticeMinutes = DefaultNoticeMinutes,
            MaximumAdvanceBookingDays = DefaultHorizonDays
        });
    }

    /// <summary>Blocking appointments that could touch the searched dates. An appointment with no end time occupies its
    /// procedure's consultation duration, or the clinic default.</summary>
    private async Task<List<(DateTimeOffset Start, DateTimeOffset End)>> LoadBusyAsync(
        Guid clinicId, Ctx ctx, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var lowUtc = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var highUtc = new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var rows = await _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Rescheduled
                        && a.ScheduledStart >= lowUtc && a.ScheduledStart < highUtc)
            .Select(a => new { a.ScheduledStart, a.ScheduledEnd, ProcedureMinutes = a.Procedure != null ? a.Procedure.ConsultationDuration : null })
            .ToListAsync(ct);

        return rows.Select(a => (a.ScheduledStart,
            a.ScheduledEnd ?? a.ScheduledStart.AddMinutes(a.ProcedureMinutes is > 0 ? a.ProcedureMinutes.Value : ctx.Booking.DefaultConsultationDurationMinutes)))
            .ToList();
    }

    /// <summary>Interval overlap with each existing appointment widened by the buffer on both sides. freeFrom is the earliest
    /// moment a slot could start after everything that blocks this one.</summary>
    private static bool IsBlocked(
        List<(DateTimeOffset Start, DateTimeOffset End)> busy, int bufferMinutes,
        DateTimeOffset slotStart, DateTimeOffset slotEnd, out DateTimeOffset freeFrom)
    {
        var buffer = TimeSpan.FromMinutes(bufferMinutes);
        freeFrom = default;
        var blocked = false;
        foreach (var b in busy)
        {
            if (slotStart < b.End + buffer && slotEnd > b.Start - buffer)
            {
                blocked = true;
                var free = b.End + buffer;
                if (free > freeFrom) freeFrom = free;
            }
        }
        return blocked;
    }

    private sealed class Ctx
    {
        private readonly Dictionary<DateOnly, ClinicAvailabilityException> _exceptions = new();

        public Ctx(TimeZoneInfo tz, List<ClinicAvailabilityRule> rules, ClinicBookingSettings booking)
        {
            Tz = tz;
            AllRules = rules;
            Booking = booking;
        }

        public TimeZoneInfo Tz { get; }
        public ClinicBookingSettings Booking { get; }
        public List<ClinicAvailabilityRule> AllRules { get; }

        /// <summary>Configured = at least one weekday is open. Nothing is ever offered for an unconfigured clinic.</summary>
        public bool Configured => AllRules.Any(r => r.IsOpen);

        public async Task LoadExceptionsAsync(ApplicationDbContext db, Guid clinicId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var rows = await db.ClinicAvailabilityExceptions.AsNoTracking()
                .Where(e => e.ClinicId == clinicId && e.Date >= from && e.Date <= to)
                .ToListAsync(ct);
            _exceptions.Clear();
            foreach (var e in rows) _exceptions[e.Date] = e;
        }

        /// <summary>The open windows for a local date: a date exception replaces the weekly schedule entirely.</summary>
        public IEnumerable<(TimeOnly Start, TimeOnly End)> Windows(DateOnly date)
        {
            if (_exceptions.TryGetValue(date, out var ex))
            {
                if (ex.IsClosed || ex.StartTime is null || ex.EndTime is null) return Array.Empty<(TimeOnly, TimeOnly)>();
                return new[] { (ex.StartTime.Value, ex.EndTime.Value) };
            }

            return AllRules
                .Where(r => r.IsOpen && r.DayOfWeek == (int)date.DayOfWeek)
                .OrderBy(r => r.StartTime)
                .Select(r => (r.StartTime, r.EndTime))
                .ToList();
        }
    }
}
