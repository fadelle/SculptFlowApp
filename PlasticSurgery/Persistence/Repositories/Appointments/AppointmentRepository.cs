using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Appointments;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Appointments;

namespace PlasticSurgery.Persistence.Repositories.Appointments;

public class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _db;

    public AppointmentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Appointment appointment) => _db.Appointments.Add(appointment);

    public Task<Appointment?> GetAsync(Guid clinicId, Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments.FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == appointmentId, ct);

    public Task<Appointment?> GetWithDetailsAsync(Guid clinicId, Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Procedure)
            .FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == appointmentId, ct);

    public Task<Appointment?> GetForLeadAsync(Guid clinicId, Guid leadId, Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments.FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.LeadId == leadId && a.Id == appointmentId, ct);

    public Task<Appointment?> GetWithProcedureReadOnlyAsync(Guid appointmentId, CancellationToken ct = default) =>
        _db.Appointments.AsNoTracking().Include(a => a.Procedure).FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

    public async Task<IReadOnlyList<Appointment>> ListUpcomingForLeadAsync(Guid clinicId, Guid leadId, DateTimeOffset now,
        CancellationToken ct = default) =>
        await _db.Appointments.AsNoTracking()
            .Include(a => a.Procedure)
            .Where(a => a.ClinicId == clinicId && a.LeadId == leadId && a.ScheduledStart > now
                        && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.ScheduledStart)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Appointment> Items, int TotalCount)> ListAsync(Guid clinicId, string? status,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Procedure)
            .Where(a => a.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (from.HasValue) query = query.Where(a => a.ScheduledStart >= from);
        if (to.HasValue) query = query.Where(a => a.ScheduledStart <= to);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(a => a.ScheduledStart).Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Appointment>> ListStartingBetweenAsync(Guid clinicId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken ct = default) =>
        await _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Procedure)
            .Where(a => a.ClinicId == clinicId && a.ScheduledStart >= fromUtc && a.ScheduledStart < toUtc)
            .OrderBy(a => a.ScheduledStart)
            .ToListAsync(ct);

    public Task<int> CountNeedingOutcomeAsync(Guid clinicId, DateTimeOffset now, CancellationToken ct = default) =>
        _db.Appointments.CountAsync(a => a.ClinicId == clinicId && a.ScheduledStart < now
            && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed), ct);

    public async Task<IReadOnlyList<BusyAppointmentRow>> ListBusyAsync(Guid clinicId, DateTimeOffset lowUtc, DateTimeOffset highUtc,
        Guid? excludeAppointmentId, CancellationToken ct = default) =>
        await _db.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinicId && (excludeAppointmentId == null || a.Id != excludeAppointmentId)
                        && a.Status != AppointmentStatus.Canceled && a.Status != AppointmentStatus.Rescheduled
                        && a.ScheduledStart >= lowUtc && a.ScheduledStart < highUtc)
            .Select(a => new BusyAppointmentRow(a.ScheduledStart, a.ScheduledEnd,
                a.Procedure != null ? a.Procedure.ConsultationDuration : null))
            .ToListAsync(ct);

    public async Task LoadDetailsAsync(Appointment appointment, CancellationToken ct = default)
    {
        await _db.Entry(appointment).Reference(x => x.Lead).LoadAsync(ct);
        if (appointment.ProcedureId is not null)
        {
            await _db.Entry(appointment).Reference(x => x.Procedure).LoadAsync(ct);
        }
    }

    public Task LockClinicForBookingAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtext({clinicId.ToString()}))", ct);
}
