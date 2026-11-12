using Omni.App.Common.Enums;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Bookings;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Catalog;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Services.Bookings;

public class AvailabilityService : IAvailabilityService
{
    // The member's wording of each note (QA t5-ops-22); the AI tool keeps its own Message.
    private const string PastKey = "bookings.available.past";
    private const string NotConfiguredKey = "bookings.available.notConfigured";
    private const string TooFarKey = "bookings.available.tooFar";
    private const string NoneKey = "bookings.available.none";

    // Defaults for clients without their own booking settings, read through IConfigManager (config.settings, else
    // the constants in Common/Statics/ConfigDefaults).
    private int DefaultDurationMinutes => _config.AvailabilityDefaultDurationMinutes;
    private int DefaultBufferMinutes => _config.AvailabilityDefaultBufferMinutes;
    private int DefaultNoticeMinutes => _config.AvailabilityDefaultNoticeMinutes;
    private int DefaultHorizonDays => _config.AvailabilityDefaultHorizonDays;
    private int MaxRangeDays => _config.AvailabilityMaxRangeDays;

    private readonly IAvailabilityRepository _availability;
    private readonly IClientRepository _clients;
    private readonly IServiceRepository _services;
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;

    public AvailabilityService(IAvailabilityRepository availability, IClientRepository clients, IServiceRepository services, IBookingRepository bookings, IUnitOfWork unitOfWork,
        IConfigManager config)
    {
        _availability = availability;
        _clients = clients;
        _services = services;
        _bookings = bookings;
        _unitOfWork = unitOfWork;
        _config = config;
    }

    // ------------------------------------------------------------------ slots

    public async Task<AvailabilityResponse> GetSlotsAsync(
        Guid clientId, Guid? serviceId, DateOnly? from, int days, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clientId, ct);
        if (ctx is null) return NotConfigured("UTC", DefaultDurationMinutes, DateOnly.FromDateTime(DateTime.UtcNow), "Client not found.");

        var duration = await ResolveDurationAsync(clientId, serviceId, ctx.Settings.DefaultBookingDurationMinutes, ct);
        var nowUtc = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, ctx.Tz).DateTime);

        if (!ctx.Configured)
        {
            return NotConfigured(ctx.Tz.Id, duration, today,
                "This client hasn't set up its booking availability yet, so no times can be offered.") with { MessageKey = NotConfiguredKey };
        }

        // A past date is almost always the caller working from the wrong "today". Don't silently search other dates —
        // say so, so the AI can correct itself using Today/Tomorrow in this response.
        if (from is { } requested && requested < today)
        {
            return new AvailabilityResponse(true, ctx.Tz.Id, today.ToString("yyyy-MM-dd"), duration,
                requested.ToString("yyyy-MM-dd"), requested.ToString("yyyy-MM-dd"),
                $"The requested date {requested:yyyy-MM-dd} is in the past. Today is {today:yyyy-MM-dd} ({today.DayOfWeek}). " +
                "Resolve the contact's day from today's date and call again with a date that is today or later.",
                Array.Empty<AvailableSlotResponse>()) { MessageKey = PastKey };
        }

        days = Math.Clamp(days, 1, MaxRangeDays);
        var start = from ?? today;
        var maxDate = today.AddDays(ctx.Settings.MaximumAdvanceBookingDays);
        var end = start.AddDays(days - 1);
        if (end > maxDate) end = maxDate;

        if (start > maxDate)
        {
            return new AvailabilityResponse(true, ctx.Tz.Id, today.ToString("yyyy-MM-dd"), duration, start.ToString("yyyy-MM-dd"), start.ToString("yyyy-MM-dd"),
                $"Bookings can only be made up to {ctx.Settings.MaximumAdvanceBookingDays} days ahead.", Array.Empty<AvailableSlotResponse>())
            {
                MessageKey = TooFarKey, MessageArgs = [ctx.Settings.MaximumAdvanceBookingDays]
            };
        }

        // From the day before: its late hours can run past midnight into the first day of the range (QA t12-fix-07).
        await ctx.LoadExceptionsAsync(_availability, clientId, start.AddDays(-1), end, ct);
        var busy = await LoadBusyAsync(clientId, ctx, start, end.AddDays(1), ct);

        var buffer = ctx.Settings.BufferMinutes;
        var step = duration + buffer;
        var earliest = nowUtc.AddMinutes(ctx.Settings.MinimumBookingNoticeMinutes);
        var slots = new List<AvailableSlotResponse>();

        for (var date = start.AddDays(-1); date <= end; date = date.AddDays(1))
        {
            foreach (var (spanStart, spanEnd) in ctx.Spans(date))
            {
                var cursor = spanStart;
                var limit = spanEnd;

                // Next-fit: take a slot, advance by duration + buffer; when an existing booking (plus buffer) is in the way,
                // jump to the first moment after it. Keeps slots on a tidy grid while still using the gaps around off-grid bookings.
                while (cursor.AddMinutes(duration) <= limit)
                {
                    if (ctx.Tz.IsInvalidTime(cursor)) { cursor = cursor.AddMinutes(step); continue; }

                    var slotStart = new DateTimeOffset(cursor, ctx.Tz.GetUtcOffset(cursor));
                    var slotEnd = slotStart.AddMinutes(duration);

                    if (slotStart < earliest) { cursor = cursor.AddMinutes(step); continue; }
                    // A slot is listed under the day it starts on: the day before's late hours only count from the range's start.
                    var slotDate = DateOnly.FromDateTime(cursor);
                    if (slotDate < start) { cursor = cursor.AddMinutes(step); continue; }
                    if (slotDate > maxDate) break;

                    if (IsBlocked(busy, buffer, slotStart, slotEnd, out var freeFrom))
                    {
                        var freeLocal = TimeZoneInfo.ConvertTime(freeFrom, ctx.Tz).DateTime;
                        cursor = freeLocal > cursor ? freeLocal : cursor.AddMinutes(step);
                        continue;
                    }

                    slots.Add(new AvailableSlotResponse(slotStart, slotEnd,
                        slotDate.ToString("yyyy-MM-dd"), cursor.ToString("HH:mm"),
                        $"{slotDate.DayOfWeek} {cursor.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}"));
                    cursor = cursor.AddMinutes(step);
                }
            }
        }

        var message = slots.Count == 0 ? "No available times in this date range." : null;
        return new AvailabilityResponse(true, ctx.Tz.Id, today.ToString("yyyy-MM-dd"), duration, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), message, slots)
        {
            MessageKey = slots.Count == 0 ? NoneKey : null
        };
    }

    public async Task<SlotCheck> CheckSlotAsync(
        Guid clientId, Guid? serviceId, DateTimeOffset start, DateTimeOffset? end, Guid? excludeBookingId = null, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clientId, ct);
        var fallbackEnd = end ?? start.AddMinutes(DefaultDurationMinutes);
        if (ctx is null) return new SlotCheck("Client not found.", fallbackEnd, SlotCheckReason.NotConfigured);
        if (!ctx.Configured) return new SlotCheck("This client hasn't set up its booking availability yet.", fallbackEnd, SlotCheckReason.NotConfigured);

        var duration = await ResolveDurationAsync(clientId, serviceId, ctx.Settings.DefaultBookingDurationMinutes, ct);
        var slotEnd = end ?? start.AddMinutes(duration);
        if (slotEnd <= start) return new SlotCheck("The booking must end after it starts.", slotEnd, SlotCheckReason.EndBeforeStart);

        var nowUtc = DateTimeOffset.UtcNow;
        if (start < nowUtc.AddMinutes(ctx.Settings.MinimumBookingNoticeMinutes))
        {
            return new SlotCheck("That time is too soon - it is inside the client's minimum booking notice.", slotEnd, start < nowUtc ? SlotCheckReason.Past : SlotCheckReason.Notice);
        }

        var startLocal = TimeZoneInfo.ConvertTime(start, ctx.Tz).DateTime;
        var endLocal = TimeZoneInfo.ConvertTime(slotEnd, ctx.Tz).DateTime;
        var date = DateOnly.FromDateTime(startLocal);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, ctx.Tz).DateTime);
        if (date > today.AddDays(ctx.Settings.MaximumAdvanceBookingDays))
        {
            return new SlotCheck($"Bookings can only be made up to {ctx.Settings.MaximumAdvanceBookingDays} days ahead.", slotEnd, SlotCheckReason.Horizon);
        }

        // The day before too: a booking after midnight can sit in its late hours (QA t12-fix-07).
        await ctx.LoadExceptionsAsync(_availability, clientId, date.AddDays(-1), date, ct);
        var inWindow = new[] { date.AddDays(-1), date }.SelectMany(ctx.Spans).Any(w => startLocal >= w.Start && endLocal <= w.End);
        if (!inWindow) return new SlotCheck("The client is not open for a booking at that time.", slotEnd, SlotCheckReason.Closed);

        var busy = await LoadBusyAsync(clientId, ctx, date, date.AddDays(1), ct, excludeBookingId);
        if (IsBlocked(busy, ctx.Settings.BufferMinutes, start, slotEnd, out _))
        {
            return new SlotCheck("That time is no longer available - it overlaps another booking.", slotEnd, SlotCheckReason.Overlap);
        }

        return new SlotCheck(null, slotEnd);
    }

    public async Task<bool> OverlapsBookingAsync(Guid clientId, DateTimeOffset start, DateTimeOffset end, Guid? excludeBookingId = null,
        CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clientId, ct);
        var defaultMinutes = ctx?.Settings.DefaultBookingDurationMinutes ?? DefaultDurationMinutes;
        // A booking that started up to a day before can still run into this one (a booking is at most a day long).
        var rows = await _bookings.ListBusyAsync(clientId, start.AddDays(-1).ToUniversalTime(), end.ToUniversalTime(), excludeBookingId, ct);
        return rows.Any(b => start < (b.ScheduledEnd ?? b.ScheduledStart.AddMinutes(b.ServiceMinutes is > 0 ? b.ServiceMinutes.Value : defaultMinutes))
            && end > b.ScheduledStart);
    }

    // ------------------------------------------------------------------ settings (Business Info → Availability)

    public async Task<AvailabilitySettingsDto> GetSettingsAsync(Guid clientId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(clientId, ct)
                  ?? throw new ArgumentException("clients.errors.notFound");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ctx.Tz).DateTime);

        var days = Enumerable.Range(0, 7).Select(dow =>
        {
            var rules = ctx.AllRules.Where(r => r.DayOfWeek == dow).OrderBy(r => r.StartTime).ToList();
            if (rules.Count == 0) return new DayRuleDto(dow, false, "09:00", "17:00");
            var first = rules[0];
            var second = first.IsOpen ? rules.Skip(1).FirstOrDefault(r => r.IsOpen) : null;
            return new DayRuleDto(dow, first.IsOpen, Fmt(first.StartTime), Fmt(first.EndTime),
                second is null ? null : Fmt(second.StartTime), second is null ? null : Fmt(second.EndTime));
        }).ToList();

        var exceptions = await _availability.ListExceptionsFromAsync(clientId, today, ct);

        return new AvailabilitySettingsDto(
            ctx.Tz.Id,
            ctx.Configured,
            days,
            new BookingRulesDto(ctx.Settings.DefaultBookingDurationMinutes, ctx.Settings.BufferMinutes,
                ctx.Settings.MinimumBookingNoticeMinutes, ctx.Settings.MaximumAdvanceBookingDays),
            exceptions.Select(e => new AvailabilityExceptionDto(
                e.Id, e.Date.ToString("yyyy-MM-dd"), e.IsClosed,
                e.StartTime is null ? null : Fmt(e.StartTime.Value), e.EndTime is null ? null : Fmt(e.EndTime.Value), e.Reason)).ToList());
    }

    public async Task SaveScheduleAsync(
        Guid clientId, string timezone, IReadOnlyList<DayRuleDto> days, BookingRulesDto bookingRules, CancellationToken ct = default)
    {
        var client = await _clients.GetAsync(clientId, ct)
                     ?? throw new ArgumentException("clients.errors.notFound");

        timezone = (timezone ?? string.Empty).Trim();
        try { TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { throw new ArgumentException("availability.errors.unknownTimeZone"); }

        if (bookingRules.DefaultDurationMinutes is < 5 or > 480) throw new ArgumentException("availability.errors.durationRange");
        if (bookingRules.BufferMinutes is < 0 or > 240) throw new ArgumentException("availability.errors.bufferRange");
        if (bookingRules.MinimumNoticeMinutes is < 0 or > 60 * 24 * 30) throw new ArgumentException("availability.errors.noticeRange");
        if (bookingRules.MaxAdvanceDays is < 1 or > 365) throw new ArgumentException("availability.errors.advanceRange");

        // Each day: one window, or two when it has a second one after the first (QA t5-ops-18). A closed day keeps one row.
        var parsed = new List<(int Dow, bool Open, List<(TimeOnly Start, TimeOnly End)> Windows)>();
        foreach (var d in days)
        {
            if (d.DayOfWeek is < 0 or > 6) throw new ArgumentException("availability.errors.invalidDay");
            var okStart = TimeOnly.TryParse(d.Start, out var s);
            var okEnd = TimeOnly.TryParse(d.End, out var e);
            var windows = new List<(TimeOnly, TimeOnly)>();
            if (d.IsOpen)
            {
                var name = TextArg.Of("days." + ((DayOfWeek)d.DayOfWeek).ToString().ToLowerInvariant());
                if (!okStart || !okEnd) throw new ArgumentException("availability.errors.dayTimesRequired").WithArgs(name);
                // An end before the start closes the next day, e.g. 20:00 to 02:00 (QA t12-fix-07); the same time is no window.
                if (e == s) throw new ArgumentException("availability.errors.dayEndSameAsStart").WithArgs(name);
                windows.Add((s, e));
                var hasStart2 = !string.IsNullOrWhiteSpace(d.Start2);
                var hasEnd2 = !string.IsNullOrWhiteSpace(d.End2);
                if (hasStart2 || hasEnd2)
                {
                    if (!TimeOnly.TryParse(d.Start2, out var s2) || !TimeOnly.TryParse(d.End2, out var e2))
                        throw new ArgumentException("availability.errors.secondWindowIncomplete").WithArgs(name);
                    // Only the day's last hours may run past midnight.
                    if (e < s) throw new ArgumentException("availability.errors.overnightLast").WithArgs(name);
                    if (e2 == s2) throw new ArgumentException("availability.errors.dayEndSameAsStart").WithArgs(name);
                    if (s2 < e) throw new ArgumentException("availability.errors.secondWindowOrder").WithArgs(name);
                    windows.Add((s2, e2));
                }
            }
            else
            {
                // A closed day still stores a valid window (the times are kept for when it opens again).
                windows.Add(okStart && okEnd && e != s ? (s, e) : (new TimeOnly(9, 0), new TimeOnly(17, 0)));
            }
            parsed.Add((d.DayOfWeek, d.IsOpen, windows));
        }
        if (parsed.Select(p => p.Dow).Distinct().Count() != parsed.Count) throw new ArgumentException("availability.errors.duplicateDay");
        // Late hours that run past midnight must end before the next day opens (QA t12-fix-07).
        foreach (var p in parsed.Where(p => p.Open && p.Windows[^1].End < p.Windows[^1].Start))
        {
            var next = parsed.FirstOrDefault(n => n.Dow == (p.Dow + 1) % 7);
            if (next.Windows is { Count: > 0 } && next.Open && next.Windows[0].Start < p.Windows[^1].End)
            {
                throw new ArgumentException("availability.errors.overnightOverlap").WithArgs(
                    TextArg.Of("days." + ((DayOfWeek)p.Dow).ToString().ToLowerInvariant()),
                    TextArg.Of("days." + ((DayOfWeek)next.Dow).ToString().ToLowerInvariant()));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var rules = await _availability.ListRulesAsync(clientId, ct);
        foreach (var p in parsed)
        {
            // Reuse the day's rows in start order, add what's missing, remove what's left over.
            var existing = rules.Where(r => r.DayOfWeek == p.Dow).OrderBy(r => r.StartTime).ToList();
            for (var w = 0; w < p.Windows.Count; w++)
            {
                var (start, end) = p.Windows[w];
                if (w < existing.Count)
                {
                    var rule = existing[w];
                    rule.IsOpen = p.Open; rule.StartTime = start; rule.EndTime = end; rule.UpdatedAt = now;
                }
                else
                {
                    _availability.AddRule(new AvailabilityRule
                    {
                        Id = Guid.NewGuid(), ClientId = clientId, DayOfWeek = p.Dow, IsOpen = p.Open,
                        StartTime = start, EndTime = end, CreatedAt = now, UpdatedAt = now
                    });
                }
            }
            foreach (var extra in existing.Skip(p.Windows.Count)) _availability.RemoveRule(extra);
        }

        var settings = await _availability.GetBookingSettingsAsync(clientId, ct);
        if (settings is null)
        {
            settings = new BookingSettings { Id = Guid.NewGuid(), ClientId = clientId, CreatedAt = now };
            _availability.AddBookingSettings(settings);
        }
        settings.DefaultBookingDurationMinutes = bookingRules.DefaultDurationMinutes;
        settings.BufferMinutes = bookingRules.BufferMinutes;
        settings.MinimumBookingNoticeMinutes = bookingRules.MinimumNoticeMinutes;
        settings.MaximumAdvanceBookingDays = bookingRules.MaxAdvanceDays;
        settings.UpdatedAt = now;

        client.Timezone = timezone;
        client.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task SaveExceptionAsync(
        Guid clientId, DateOnly date, bool isClosed, string? start, string? end, string? reason, CancellationToken ct = default)
    {
        TimeOnly? s = null, e = null;
        if (!isClosed)
        {
            if (!TimeOnly.TryParse(start, out var ps) || !TimeOnly.TryParse(end, out var pe))
                throw new ArgumentException("availability.errors.dateTimesRequired");
            if (pe <= ps) throw new ArgumentException("availability.errors.endBeforeStart");
            s = ps; e = pe;
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > 200 }) throw new ArgumentException("availability.errors.reasonTooLong");

        var now = DateTimeOffset.UtcNow;
        var existing = await _availability.GetExceptionByDateAsync(clientId, date, ct);
        if (existing is null)
        {
            existing = new AvailabilityException { Id = Guid.NewGuid(), ClientId = clientId, Date = date, CreatedAt = now };
            _availability.AddException(existing);
        }
        existing.IsClosed = isClosed;
        existing.StartTime = s;
        existing.EndTime = e;
        existing.Reason = reason;
        existing.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task DeleteExceptionAsync(Guid clientId, Guid exceptionId, CancellationToken ct = default)
    {
        var existing = await _availability.GetExceptionAsync(clientId, exceptionId, ct);
        if (existing is null) return;
        _availability.RemoveException(existing);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private static string Fmt(TimeOnly t) => t.ToString("HH:mm");

    private static AvailabilityResponse NotConfigured(string tz, int duration, DateOnly today, string message) =>
        new(false, tz, today.ToString("yyyy-MM-dd"), duration, string.Empty, string.Empty, message, Array.Empty<AvailableSlotResponse>());

    /// <summary>Service's own booking duration when it is active and has one; otherwise the client default.
    /// A service that is unknown for this client or inactive is rejected — it can't be newly booked.</summary>
    private async Task<int> ResolveDurationAsync(Guid clientId, Guid? serviceId, int clientDefault, CancellationToken ct)
    {
        if (serviceId is null) return clientDefault;

        var service = await _services.GetReadOnlyAsync(clientId, serviceId.Value, ct);

        if (service is null) throw new ArgumentException("services.errors.notFound");
        if (!service.IsActive) throw new ArgumentException("services.errors.inactiveForBooking").WithArgs(service.Name);

        return service.BookingDuration is > 0 ? service.BookingDuration.Value : clientDefault;
    }

    private async Task<Ctx?> LoadAsync(Guid clientId, CancellationToken ct)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        if (client is null) return null;

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(client.Timezone); }
        catch { tz = TimeZoneInfo.Utc; }

        var rules = await _availability.ListRulesReadOnlyAsync(clientId, ct);
        var settings = await _availability.GetBookingSettingsReadOnlyAsync(clientId, ct);

        return new Ctx(tz, rules, settings ?? new BookingSettings
        {
            DefaultBookingDurationMinutes = DefaultDurationMinutes,
            BufferMinutes = DefaultBufferMinutes,
            MinimumBookingNoticeMinutes = DefaultNoticeMinutes,
            MaximumAdvanceBookingDays = DefaultHorizonDays
        });
    }

    /// <summary>Blocking bookings that could touch the searched dates. A booking with no end time occupies its
    /// service's booking duration, or the client default.</summary>
    private async Task<List<(DateTimeOffset Start, DateTimeOffset End)>> LoadBusyAsync(
        Guid clientId, Ctx ctx, DateOnly from, DateOnly to, CancellationToken ct, Guid? excludeBookingId = null)
    {
        var lowUtc = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var highUtc = new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var rows = await _bookings.ListBusyAsync(clientId, lowUtc, highUtc, excludeBookingId, ct);

        return rows.Select(a => (a.ScheduledStart,
            a.ScheduledEnd ?? a.ScheduledStart.AddMinutes(a.ServiceMinutes is > 0 ? a.ServiceMinutes.Value : ctx.Settings.DefaultBookingDurationMinutes)))
            .ToList();
    }

    /// <summary>Interval overlap with each existing booking widened by the buffer on both sides. freeFrom is the earliest
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
        private readonly Dictionary<DateOnly, AvailabilityException> _exceptions = new();

        public Ctx(TimeZoneInfo tz, List<AvailabilityRule> rules, BookingSettings settings)
        {
            Tz = tz;
            AllRules = rules;
            Settings = settings;
        }

        public TimeZoneInfo Tz { get; }
        public BookingSettings Settings { get; }
        public List<AvailabilityRule> AllRules { get; }

        /// <summary>Configured = at least one weekday is open. Nothing is ever offered for an unconfigured client.</summary>
        public bool Configured => AllRules.Any(r => r.IsOpen);

        public async Task LoadExceptionsAsync(IAvailabilityRepository availability, Guid clientId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var rows = await availability.ListExceptionsAsync(clientId, from, to, ct);
            _exceptions.Clear();
            foreach (var e in rows) _exceptions[e.Date] = e;
        }

        /// <summary>The open windows of a local date as local start/end moments: a window whose end is before its start closes
        /// the next day (QA t12-fix-07: 20:00 to 02:00).</summary>
        public IEnumerable<(DateTime Start, DateTime End)> Spans(DateOnly date) =>
            Windows(date).Select(w => (date.ToDateTime(w.Start), w.End > w.Start ? date.ToDateTime(w.End) : date.AddDays(1).ToDateTime(w.End))).ToList();

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
