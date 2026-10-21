using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Bookings;

namespace Omni.App.Persistence.Repositories.Bookings;

public class BookingReminderRepository : IBookingReminderRepository
{
    private readonly ApplicationDbContext _db;

    public BookingReminderRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<ReminderSettings?> GetSettingsAsync(Guid clientId, CancellationToken ct = default) =>
        _db.ReminderSettings.FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public Task<ReminderSettings?> GetSettingsReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _db.ReminderSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public void AddSettings(ReminderSettings settings) => _db.ReminderSettings.Add(settings);

    public async Task<IReadOnlyList<Guid>> ListEnabledClientIdsAsync(CancellationToken ct = default) =>
        await (from s in _db.ReminderSettings.AsNoTracking()
               join c in _db.Clients.AsNoTracking() on s.ClientId equals c.Id
               where s.IsEnabled && c.IsActive && (s.FirstHoursBefore > 0 || s.SecondHoursBefore > 0)
               select s.ClientId).ToListAsync(ct);

    public async Task<IReadOnlyList<DueReminder>> ListDueAsync(Guid clientId, string kind, int hoursBefore, DateTimeOffset now, int limit,
        CancellationToken ct = default)
    {
        var until = now.AddHours(hoursBefore);
        return await _db.Bookings.AsNoTracking()
            .Where(b => b.ClientId == clientId
                && (b.Status == BookingStatus.Booked || b.Status == BookingStatus.Confirmed)
                && b.ScheduledStart > now && b.ScheduledStart <= until
                && !_db.BookingReminders.Any(r => r.BookingId == b.Id && r.Kind == kind && r.ScheduledStart == b.ScheduledStart))
            .OrderBy(b => b.ScheduledStart)
            .Take(limit)
            .Select(b => new DueReminder(b.Id, b.ContactId, b.ScheduledStart, b.CreatedAt, b.Service != null ? b.Service.Name : null))
            .ToListAsync(ct);
    }

    public void AddReminder(BookingReminder reminder) => _db.BookingReminders.Add(reminder);

    public Task CompleteReminderAsync(Guid reminderId, string status, string? reason, string? channel, Guid? messageId, DateTimeOffset now,
        CancellationToken ct = default) =>
        _db.BookingReminders.Where(r => r.Id == reminderId).ExecuteUpdateAsync(u => u
            .SetProperty(r => r.Status, status)
            .SetProperty(r => r.Reason, reason)
            .SetProperty(r => r.Channel, channel)
            .SetProperty(r => r.MessageId, messageId)
            .SetProperty(r => r.UpdatedAt, now), ct);

    public async Task<IReadOnlyList<BookingReminderRow>> ListForBookingAsync(Guid clientId, Guid bookingId, CancellationToken ct = default) =>
        await _db.BookingReminders.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.BookingId == bookingId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new BookingReminderRow(r.Kind, r.ScheduledStart, r.Status, r.Reason, r.Channel, r.UpdatedAt))
            .ToListAsync(ct);
}
