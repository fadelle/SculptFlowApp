using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class AppointmentService : IAppointmentService
{
    private readonly ApplicationDbContext _db;
    private readonly IEventLogger _events;
    private readonly IProcedureService _procedures;

    public AppointmentService(ApplicationDbContext db, IEventLogger events, IProcedureService procedures)
    {
        _db = db;
        _events = events;
        _procedures = procedures;
    }

    public async Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(Guid clinicId, int days, CancellationToken ct = default)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return Array.Empty<AvailableSlotResponse>();

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(clinic.Timezone);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var windowStartUtc = DateTimeOffset.UtcNow;
        var windowEndUtc = DateTimeOffset.UtcNow.AddDays(days);

        var busy = await _db.Appointments
            .Where(a => a.ClinicId == clinicId
                        && a.Status != AppointmentStatus.Canceled
                        && a.ScheduledStart >= windowStartUtc
                        && a.ScheduledStart <= windowEndUtc)
            .Select(a => new { a.ScheduledStart, End = a.ScheduledEnd ?? a.ScheduledStart.AddMinutes(30) })
            .ToListAsync(ct);

        var slots = new List<AvailableSlotResponse>();

        for (var dayOffset = 0; dayOffset < days; dayOffset++)
        {
            var dayLocal = nowLocal.Date.AddDays(dayOffset);

            for (var hour = 9; hour < 17; hour++)
            {
                var slotLocal = new DateTimeOffset(dayLocal.AddHours(hour), tz.GetUtcOffset(dayLocal.AddHours(hour)));
                var slotStartUtc = slotLocal.ToUniversalTime();
                var slotEndUtc = slotStartUtc.AddHours(1);

                if (slotStartUtc <= DateTimeOffset.UtcNow) continue;

                var overlaps = busy.Any(b => b.ScheduledStart < slotEndUtc && b.End > slotStartUtc);
                if (!overlaps)
                {
                    slots.Add(new AvailableSlotResponse(slotStartUtc, slotEndUtc));
                }
            }
        }

        return slots;
    }

    public async Task<AppointmentResponse> CreateAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        // A new booking may only reference one of this clinic's ACTIVE procedures.
        if (request.ProcedureId is { } procedureId)
        {
            await _procedures.EnsureUsableAsync(request.ClinicId, procedureId, ct);
        }

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            LeadId = request.LeadId,
            ProcedureId = request.ProcedureId,
            AppointmentType = string.IsNullOrWhiteSpace(request.AppointmentType) ? "consultation" : request.AppointmentType,
            Status = AppointmentStatus.Booked,
            ScheduledStart = request.ScheduledStart,
            ScheduledEnd = request.ScheduledEnd,
            LocationType = request.LocationType,
            LocationName = request.LocationName,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Appointments.Add(appointment);

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId && l.ClinicId == request.ClinicId, ct);
        if (lead is not null)
        {
            lead.Status = LeadStatus.ConsultationBooked;
            lead.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _events.Log(request.ClinicId, EventTypes.ConsultationBooked, leadId: request.LeadId, appointmentId: appointment.Id);

        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(appointment, ct);
    }

    public async Task<AppointmentResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var appointment = await _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Procedure)
            .FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id, ct);
        return appointment is null ? null : ToResponse(appointment);
    }

    public async Task<AppointmentResponse?> UpdateStatusAsync(Guid clinicId, Guid id, string status, CancellationToken ct = default)
    {
        if (!AppointmentStatus.All.Contains(status))
        {
            throw new ArgumentException($"Invalid appointment status '{status}'.", nameof(status));
        }

        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id, ct);
        if (appointment is null) return null;

        appointment.Status = status;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == appointment.LeadId, ct);
        if (lead is not null)
        {
            if (status == AppointmentStatus.Attended)
            {
                lead.Status = LeadStatus.ConsultationAttended;
                _events.Log(clinicId, EventTypes.ConsultationAttended, leadId: lead.Id, appointmentId: appointment.Id);
            }
            else if (status == AppointmentStatus.NoShow)
            {
                lead.Status = LeadStatus.NoShow;
                _events.Log(clinicId, EventTypes.ConsultationNoShow, leadId: lead.Id, appointmentId: appointment.Id);
            }
            lead.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return await ToResponseAsync(appointment, ct);
    }

    public async Task<AppointmentResponse?> RescheduleAsync(
        Guid clinicId, Guid id, DateTimeOffset newStart, DateTimeOffset? newEnd, string? reason, CancellationToken ct = default)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id, ct);
        if (appointment is null) return null;

        appointment.ScheduledStart = newStart;
        appointment.ScheduledEnd = newEnd;
        // A rescheduled time isn't "confirmed" again until the clinic/staff confirms it — same
        // status a brand-new booking starts at.
        appointment.Status = AppointmentStatus.Booked;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        _events.Log(clinicId, EventTypes.ConsultationRescheduled, leadId: appointment.LeadId, appointmentId: appointment.Id,
            metadataJson: JsonSerializer.Serialize(new { reason }));

        await _db.SaveChangesAsync(ct);
        return await ToResponseAsync(appointment, ct);
    }

    public async Task<AppointmentResponse?> CancelAsync(Guid clinicId, Guid id, string? reason, CancellationToken ct = default)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.ClinicId == clinicId && a.Id == id, ct);
        if (appointment is null) return null;

        appointment.Status = AppointmentStatus.Canceled;
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        _events.Log(clinicId, EventTypes.ConsultationCancelled, leadId: appointment.LeadId, appointmentId: appointment.Id,
            metadataJson: JsonSerializer.Serialize(new { reason }));

        await _db.SaveChangesAsync(ct);
        return await ToResponseAsync(appointment, ct);
    }

    public async Task<(IReadOnlyList<AppointmentResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
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

        return (items.Select(ToResponse).ToList(), totalCount);
    }

    private async Task<AppointmentResponse> ToResponseAsync(Appointment a, CancellationToken ct)
    {
        await _db.Entry(a).Reference(x => x.Lead).LoadAsync(ct);
        if (a.ProcedureId is not null)
        {
            await _db.Entry(a).Reference(x => x.Procedure).LoadAsync(ct);
        }
        return ToResponse(a);
    }

    private static AppointmentResponse ToResponse(Appointment a) => new(
        a.Id, a.ClinicId, a.LeadId, a.Lead?.FullName, a.ProcedureId, a.Procedure?.Name,
        a.AppointmentType, a.Status, a.ScheduledStart, a.ScheduledEnd,
        a.LocationType, a.LocationName, a.Notes, a.CreatedAt
    );
}
