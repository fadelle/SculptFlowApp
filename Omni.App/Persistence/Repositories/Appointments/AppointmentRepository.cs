using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Appointments;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Appointments;

namespace Omni.App.Persistence.Repositories.Appointments;

public class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _db;

    public AppointmentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Appointment appointment) => _db.Appointments.Add(appointment);

    public Task<Appointment?> GetAsync(Guid clientId, Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments.FirstOrDefaultAsync(a => a.ClientId == clientId && a.Id == appointmentId, ct);

    public Task<Appointment?> GetWithDetailsAsync(Guid clientId, Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.ClientId == clientId && a.Id == appointmentId, ct);

    public Task<Appointment?> GetForLeadAsync(Guid clientId, Guid leadId, Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments.FirstOrDefaultAsync(a => a.ClientId == clientId && a.LeadId == leadId && a.Id == appointmentId, ct);

    public Task<Appointment?> GetWithServiceReadOnlyAsync(Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments.AsNoTracking().Include(a => a.Service).FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

    public async Task<IReadOnlyList<Appointment>> ListUpcomingForLeadAsync(Guid clientId, Guid leadId, DateTimeOffset now,
        CancellationToken ct = default) =>
        await _db.Appointments.AsNoTracking()
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId && a.LeadId == leadId && a.ScheduledStart > now
                        && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.ScheduledStart)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Appointment> Items, int TotalCount)> ListAsync(Guid clientId, string? status,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Appointments
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

    public async Task<IReadOnlyList<Appointment>> ListStartingBetweenAsync(Guid clientId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken ct = default) =>
        await _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId && a.ScheduledStart >= fromUtc && a.ScheduledStart < toUtc)
            .OrderBy(a => a.ScheduledStart)
            .ToListAsync(ct);

    public Task<int> CountNeedingOutcomeAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default) =>
        _db.Appointments.CountAsync(a => a.ClientId == clientId && a.ScheduledStart < now
            && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed), ct);

    public async Task<IReadOnlyList<BusyAppointmentRow>> ListBusyAsync(Guid clientId, DateTimeOffset lowUtc, DateTimeOffset highUtc,
        Guid? excludeAppointmentId, CancellationToken ct = default) =>
        await _db.Appointments.AsNoTracking()
            .Where(a => a.ClientId == clientId && (excludeAppointmentId == null || a.Id != excludeAppointmentId)
                        && a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Rescheduled
                        && a.ScheduledStart >= lowUtc && a.ScheduledStart < highUtc)
            .Select(a => new BusyAppointmentRow(a.ScheduledStart, a.ScheduledEnd,
                a.Service != null ? a.Service.ConsultationDuration : null))
            .ToListAsync(ct);

    public async Task LoadDetailsAsync(Appointment appointment, CancellationToken ct = default)
    {
        await _db.Entry(appointment).Reference(x => x.Lead).LoadAsync(ct);
        if (appointment.ServiceId is not null)
        {
            await _db.Entry(appointment).Reference(x => x.Service).LoadAsync(ct);
        }
    }

    public Task LockClientForBookingAsync(Guid clientId, CancellationToken ct = default) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtext({clientId.ToString()}))", ct);
}
