using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Bookings;

namespace Omni.App.Persistence.Repositories.Bookings;

public class BookingRepository : IBookingRepository
{
    private readonly ApplicationDbContext _db;

    public BookingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Booking booking) => _db.Bookings.Add(booking);

    public Task<Booking?> GetAsync(Guid clientId, Guid bookingId, CancellationToken ct = default) =>
        _db.Bookings.FirstOrDefaultAsync(a => a.ClientId == clientId && a.Id == bookingId, ct);

    public Task<Booking?> GetWithDetailsAsync(Guid clientId, Guid bookingId, CancellationToken ct = default) =>
        _db.Bookings
            .Include(a => a.Lead)
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.ClientId == clientId && a.Id == bookingId, ct);

    public Task<Booking?> GetForLeadAsync(Guid clientId, Guid leadId, Guid bookingId, CancellationToken ct = default) =>
        _db.Bookings.FirstOrDefaultAsync(a => a.ClientId == clientId && a.LeadId == leadId && a.Id == bookingId, ct);

    public Task<Booking?> GetWithServiceReadOnlyAsync(Guid bookingId, CancellationToken ct = default) =>
        _db.Bookings.AsNoTracking().Include(a => a.Service).FirstOrDefaultAsync(a => a.Id == bookingId, ct);

    public async Task<IReadOnlyList<Booking>> ListUpcomingForLeadAsync(Guid clientId, Guid leadId, DateTimeOffset now,
        CancellationToken ct = default) =>
        await _db.Bookings.AsNoTracking()
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId && a.LeadId == leadId && a.ScheduledStart > now
                        && (a.Status == BookingStatus.Booked || a.Status == BookingStatus.Confirmed))
            .OrderBy(a => a.ScheduledStart)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Booking> Items, int TotalCount)> ListAsync(Guid clientId, string? status,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Bookings
            .Include(a => a.Lead)
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (from.HasValue) query = query.Where(a => a.ScheduledStart >= from);
        if (to.HasValue) query = query.Where(a => a.ScheduledStart <= to);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.ScheduledStart).Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Booking>> ListStartingBetweenAsync(Guid clientId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken ct = default) =>
        await _db.Bookings
            .Include(a => a.Lead)
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId && a.ScheduledStart >= fromUtc && a.ScheduledStart < toUtc)
            .OrderBy(a => a.ScheduledStart)
            .ToListAsync(ct);

    public Task<int> CountNeedingOutcomeAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default) =>
        _db.Bookings.CountAsync(a => a.ClientId == clientId && a.ScheduledStart < now
            && (a.Status == BookingStatus.Booked || a.Status == BookingStatus.Confirmed), ct);

    public async Task<IReadOnlyList<BusyBookingRow>> ListBusyAsync(Guid clientId, DateTimeOffset lowUtc, DateTimeOffset highUtc,
        Guid? excludeBookingId, CancellationToken ct = default) =>
        await _db.Bookings.AsNoTracking()
            .Where(a => a.ClientId == clientId && (excludeBookingId == null || a.Id != excludeBookingId)
                        && a.Status != BookingStatus.Canceled && a.Status != BookingStatus.Rescheduled
                        && a.ScheduledStart >= lowUtc && a.ScheduledStart < highUtc)
            .Select(a => new BusyBookingRow(a.ScheduledStart, a.ScheduledEnd,
                a.Service != null ? a.Service.BookingDuration : null))
            .ToListAsync(ct);

    public async Task LoadDetailsAsync(Booking booking, CancellationToken ct = default)
    {
        await _db.Entry(booking).Reference(x => x.Lead).LoadAsync(ct);
        if (booking.ServiceId is not null)
        {
            await _db.Entry(booking).Reference(x => x.Service).LoadAsync(ct);
        }
    }

    public Task LockClientForBookingAsync(Guid clientId, CancellationToken ct = default) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtext({clientId.ToString()}))", ct);
}
