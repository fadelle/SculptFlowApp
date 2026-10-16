using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Dashboard;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Dashboard;

namespace Omni.App.Persistence.Repositories.Dashboard;

public class DashboardRepository : IDashboardRepository
{
    private readonly ApplicationDbContext _db;

    public DashboardRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardSummaryCounts> GetSummaryCountsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var appointments = _db.Appointments.Where(a => a.ClientId == clientId);
        var orders = _db.Orders.Where(b => b.ClientId == clientId);

        var newInterestedPeople = await InRange(_db.Leads.Where(l => l.ClientId == clientId), l => l.CreatedAt, from, to).CountAsync(ct);

        var consultationsBooked = await InRange(appointments, a => a.CreatedAt, from, to).CountAsync(ct);

        var consultationsAttended = await InRange(appointments.Where(a => a.Status == AppointmentStatus.Attended), a => a.ScheduledStart, from, to)
            .CountAsync(ct);

        var surgeriesBooked = await InRange(orders, b => b.CreatedAt, from, to).CountAsync(ct);

        // Revenue lands on the service date (falling back to when the order was last updated, i.e. marked completed).
        var revenue = await InRange(orders.Where(b => b.Status == OrderStatus.Completed), b => b.ServiceDate ?? b.UpdatedAt, from, to)
            .SumAsync(b => (decimal?)b.FinalAmount, ct) ?? 0m;

        return new DashboardSummaryCounts(newInterestedPeople, consultationsBooked, consultationsAttended, surgeriesBooked, revenue);
    }

    public async Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> ListLeadsAsync(Guid clientId, string? status,
        string? search, string? source, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Leads.Include(l => l.Service).Where(l => l.ClientId == clientId);
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
                l.Id, l.FullName, l.Phone, l.Service!.Name, l.Source, l.Status,
                l.QualificationStatus, l.LastContactAt, l.NextFollowupAt, l.CreatedAt))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> ListAppointmentsAsync(Guid clientId,
        string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Appointments
            .Include(a => a.Lead)
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId);

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
                a.Id, a.Lead!.FullName, a.Lead.Phone, a.Service!.Name, a.Status, a.ScheduledStart, a.LocationType))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<DashboardServiceRow>> GetServiceStatsAsync(Guid clientId, DateTimeOffset? from,
        DateTimeOffset? to, CancellationToken ct = default)
    {
        var leads = InRange(_db.Leads.Where(l => l.ClientId == clientId), l => l.CreatedAt, from, to);
        var orders = InRange(_db.Orders.Where(b => b.ClientId == clientId), b => b.CreatedAt, from, to);
        var completed = InRange(
            _db.Orders.Where(b => b.ClientId == clientId && b.Status == OrderStatus.Completed),
            b => b.ServiceDate ?? b.UpdatedAt, from, to);

        var services = await _db.Services
            .Where(p => p.ClientId == clientId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                LeadCount = leads.Count(l => l.ServiceId == p.Id),
                OrderCount = orders.Count(b => b.ServiceId == p.Id),
                CompletedCount = completed.Count(b => b.ServiceId == p.Id),
                CompletedRevenue = completed.Where(b => b.ServiceId == p.Id).Sum(b => (decimal?)b.FinalAmount) ?? 0m
            })
            .OrderByDescending(p => p.CompletedRevenue)
            .ToListAsync(ct);

        return services
            .Select(p => new DashboardServiceRow(p.Id, p.Name, p.LeadCount, p.OrderCount, p.CompletedCount, p.CompletedRevenue))
            .ToList();
    }

    public async Task<DashboardAttentionCounts> GetAttentionCountsAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default)
    {
        var conversations = await _db.Conversations.CountAsync(c => c.ClientId == clientId
            && c.Status == ConversationStatus.Active
            && (c.Mode == ConversationMode.Human || c.Mode == ConversationMode.Approval), ct);

        var overdueFollowups = await _db.Leads.CountAsync(l => l.ClientId == clientId
            && l.NextFollowupAt != null && l.NextFollowupAt < now
            && l.Status != LeadStatus.Lost && l.Status != LeadStatus.NotInterested && l.Status != LeadStatus.SurgeryBooked, ct);

        // Same rule as the Appointments calendar's "needs an outcome" notice.
        var needsOutcome = await _db.Appointments.CountAsync(a => a.ClientId == clientId && a.ScheduledStart < now
            && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed), ct);

        return new DashboardAttentionCounts(conversations, overdueFollowups, needsOutcome);
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
