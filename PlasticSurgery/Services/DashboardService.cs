using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;

    public DashboardService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(Guid clinicId, CancellationToken ct = default)
    {
        var newInterestedPeople = await _db.Leads.CountAsync(l => l.ClinicId == clinicId, ct);

        var consultationsBooked = await _db.Appointments.CountAsync(a => a.ClinicId == clinicId, ct);

        var consultationsAttended = await _db.Appointments
            .CountAsync(a => a.ClinicId == clinicId && a.Status == AppointmentStatus.Attended, ct);

        var surgeriesBooked = await _db.ProcedureBookings.CountAsync(b => b.ClinicId == clinicId, ct);

        var revenue = await _db.ProcedureBookings
            .Where(b => b.ClinicId == clinicId && b.Status == ProcedureBookingStatus.Completed)
            .SumAsync(b => (decimal?)b.FinalAmount, ct) ?? 0m;

        // Old-lead reactivation isn't built yet (Project 8) — wire this up once that project
        // exists and reactivation attempts are tracked (e.g. via an events.event_type filter).
        var oldLeadsRecovered = 0;

        return new DashboardSummaryResponse(
            newInterestedPeople, consultationsBooked, consultationsAttended,
            surgeriesBooked, revenue, oldLeadsRecovered
        );
    }

    public async Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> GetLeadsAsync(
        Guid clinicId, string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Leads.Include(l => l.Procedure).Where(l => l.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(l => EF.Functions.ILike(l.FullName ?? "", pattern) || EF.Functions.ILike(l.Phone ?? "", pattern));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(l => new DashboardLeadRow(
                l.Id, l.FullName, l.Phone, l.Procedure!.Name, l.Source, l.Status,
                l.QualificationStatus, l.LastContactAt, l.NextFollowupAt, l.CreatedAt))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> GetAppointmentsAsync(
        Guid clinicId, string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Procedure)
            .Where(a => a.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.Lead!.FullName ?? "", pattern) || EF.Functions.ILike(a.Lead!.Phone ?? "", pattern));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(a => a.ScheduledStart)
            .Skip(skip)
            .Take(take)
            .Select(a => new DashboardAppointmentRow(
                a.Id, a.Lead!.FullName, a.Lead.Phone, a.Procedure!.Name, a.Status, a.ScheduledStart, a.LocationType))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(Guid clinicId, CancellationToken ct = default)
    {
        var procedures = await _db.Procedures
            .Where(p => p.ClinicId == clinicId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                LeadCount = _db.Leads.Count(l => l.ProcedureId == p.Id),
                BookingCount = _db.ProcedureBookings.Count(b => b.ProcedureId == p.Id),
                CompletedCount = _db.ProcedureBookings.Count(b => b.ProcedureId == p.Id && b.Status == ProcedureBookingStatus.Completed),
                CompletedRevenue = _db.ProcedureBookings
                    .Where(b => b.ProcedureId == p.Id && b.Status == ProcedureBookingStatus.Completed)
                    .Sum(b => (decimal?)b.FinalAmount) ?? 0m
            })
            .OrderByDescending(p => p.CompletedRevenue)
            .ToListAsync(ct);

        return procedures
            .Select(p => new DashboardProcedureRow(p.Id, p.Name, p.LeadCount, p.BookingCount, p.CompletedCount, p.CompletedRevenue))
            .ToList();
    }
}
