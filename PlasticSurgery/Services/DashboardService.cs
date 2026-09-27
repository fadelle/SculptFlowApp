using System.Linq.Expressions;
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

    public async Task<DashboardSummaryResponse> GetSummaryAsync(
        Guid clinicId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var appointments = _db.Appointments.Where(a => a.ClinicId == clinicId);
        var bookings = _db.ProcedureBookings.Where(b => b.ClinicId == clinicId);

        var newInterestedPeople = await InRange(_db.Leads.Where(l => l.ClinicId == clinicId), l => l.CreatedAt, from, to).CountAsync(ct);

        var consultationsBooked = await InRange(appointments, a => a.CreatedAt, from, to).CountAsync(ct);

        var consultationsAttended = await InRange(appointments.Where(a => a.Status == AppointmentStatus.Attended), a => a.ScheduledStart, from, to)
            .CountAsync(ct);

        var surgeriesBooked = await InRange(bookings, b => b.CreatedAt, from, to).CountAsync(ct);

        // Revenue lands on the procedure date (falling back to when the booking was last updated, i.e. marked completed).
        var revenue = await InRange(bookings.Where(b => b.Status == ProcedureBookingStatus.Completed), b => b.ProcedureDate ?? b.UpdatedAt, from, to)
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
        Guid clinicId, string? status, string? search, string? source, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Leads.Include(l => l.Procedure).Where(l => l.ClinicId == clinicId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalizedSource = source.Trim().ToLower();
            query = query.Where(l => l.Source != null && l.Source.ToLower() == normalizedSource);
        }
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

    public async Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(
        Guid clinicId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var leads = InRange(_db.Leads.Where(l => l.ClinicId == clinicId), l => l.CreatedAt, from, to);
        var bookings = InRange(_db.ProcedureBookings.Where(b => b.ClinicId == clinicId), b => b.CreatedAt, from, to);
        var completed = InRange(
            _db.ProcedureBookings.Where(b => b.ClinicId == clinicId && b.Status == ProcedureBookingStatus.Completed),
            b => b.ProcedureDate ?? b.UpdatedAt, from, to);

        var procedures = await _db.Procedures
            .Where(p => p.ClinicId == clinicId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                LeadCount = leads.Count(l => l.ProcedureId == p.Id),
                BookingCount = bookings.Count(b => b.ProcedureId == p.Id),
                CompletedCount = completed.Count(b => b.ProcedureId == p.Id),
                CompletedRevenue = completed.Where(b => b.ProcedureId == p.Id).Sum(b => (decimal?)b.FinalAmount) ?? 0m
            })
            .OrderByDescending(p => p.CompletedRevenue)
            .ToListAsync(ct);

        return procedures
            .Select(p => new DashboardProcedureRow(p.Id, p.Name, p.LeadCount, p.BookingCount, p.CompletedCount, p.CompletedRevenue))
            .ToList();
    }

    public async Task<DashboardAttentionResponse> GetAttentionAsync(Guid clinicId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var conversations = await _db.Conversations.CountAsync(c => c.ClinicId == clinicId
            && c.Status == ConversationStatus.Active
            && (c.Mode == ConversationMode.Human || c.Mode == ConversationMode.Approval), ct);

        var overdueFollowups = await _db.Leads.CountAsync(l => l.ClinicId == clinicId
            && l.NextFollowupAt != null && l.NextFollowupAt < now
            && l.Status != LeadStatus.Lost && l.Status != LeadStatus.NotInterested && l.Status != LeadStatus.SurgeryBooked, ct);

        // Same rule as the Appointments calendar's "needs an outcome" notice.
        var needsOutcome = await _db.Appointments.CountAsync(a => a.ClinicId == clinicId && a.ScheduledStart < now
            && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed), ct);

        return new DashboardAttentionResponse(conversations, overdueFollowups, needsOutcome);
    }

    /// <summary>Filters to rows whose date falls in [from, to); either bound may be open (null). Bounds are
    /// compared in UTC (Npgsql timestamptz parameters must be UTC).</summary>
    private static IQueryable<T> InRange<T>(IQueryable<T> query, Expression<Func<T, DateTimeOffset>> date, DateTimeOffset? from, DateTimeOffset? to)
    {
        var row = date.Parameters[0];
        if (from is not null)
        {
            query = query.Where(Expression.Lambda<Func<T, bool>>(
                Expression.GreaterThanOrEqual(date.Body, Expression.Constant(from.Value.ToUniversalTime())), row));
        }
        if (to is not null)
        {
            query = query.Where(Expression.Lambda<Func<T, bool>>(
                Expression.LessThan(date.Body, Expression.Constant(to.Value.ToUniversalTime())), row));
        }
        return query;
    }
}
