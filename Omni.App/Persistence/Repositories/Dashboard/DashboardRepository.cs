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
        var bookings = _db.Bookings.Where(a => a.ClientId == clientId);
        var orders = _db.Orders.Where(b => b.ClientId == clientId);

        var newInterestedPeople = await InRange(_db.Contacts.Where(l => l.ClientId == clientId), l => l.CreatedAt, from, to).CountAsync(ct);

        var bookingsMade = await InRange(bookings, a => a.CreatedAt, from, to).CountAsync(ct);

        var bookingsAttended = await InRange(bookings.Where(a => a.Status == BookingStatus.Attended), a => a.ScheduledStart, from, to)
            .CountAsync(ct);

        var ordersMade = await InRange(orders, b => b.CreatedAt, from, to).CountAsync(ct);

        // Revenue lands on the service date (falling back to when the order was last updated, i.e. marked completed).
        var revenue = await InRange(orders.Where(b => b.Status == OrderStatus.Completed), b => b.ServiceDate ?? b.UpdatedAt, from, to)
            .SumAsync(b => (decimal?)b.FinalAmount, ct) ?? 0m;

        return new DashboardSummaryCounts(newInterestedPeople, bookingsMade, bookingsAttended, ordersMade, revenue);
    }

    public async Task<(IReadOnlyList<DashboardContactRow> Items, int TotalCount)> ListContactsAsync(Guid clientId, string? stage,
        string? search, string? source, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Contacts.Include(l => l.Service).Where(l => l.ClientId == clientId);
        if (!string.IsNullOrWhiteSpace(stage)) query = query.Where(l => l.StageKey == stage);
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
            .Select(l => new DashboardContactRow(
                l.Id, l.FullName, l.Phone, l.Service!.Name, l.Source, l.StageKey,
                l.Qualification, l.LastContactAt, l.NextFollowupAt, l.CreatedAt))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<DashboardBookingRow> Items, int TotalCount)> ListBookingsAsync(Guid clientId,
        string? status, string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Bookings
            .Include(a => a.Contact)
            .Include(a => a.Service)
            .Where(a => a.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.Contact!.FullName ?? "", pattern) || EF.Functions.ILike(a.Contact!.Phone ?? "", pattern));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(a => a.ScheduledStart)
            .Skip(skip)
            .Take(take)
            .Select(a => new DashboardBookingRow(
                a.Id, a.Contact!.FullName, a.Contact.Phone, a.Service!.Name, a.Status, a.ScheduledStart, a.LocationType))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<DashboardServiceRow>> GetServiceStatsAsync(Guid clientId, DateTimeOffset? from,
        DateTimeOffset? to, CancellationToken ct = default)
    {
        var contacts = InRange(_db.Contacts.Where(l => l.ClientId == clientId), l => l.CreatedAt, from, to);
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
                ContactCount = contacts.Count(l => l.ServiceId == p.Id),
                OrderCount = orders.Count(b => b.ServiceId == p.Id),
                CompletedCount = completed.Count(b => b.ServiceId == p.Id),
                CompletedRevenue = completed.Where(b => b.ServiceId == p.Id).Sum(b => (decimal?)b.FinalAmount) ?? 0m
            })
            .OrderByDescending(p => p.CompletedRevenue)
            .ToListAsync(ct);

        return services
            .Select(p => new DashboardServiceRow(p.Id, p.Name, p.ContactCount, p.OrderCount, p.CompletedCount, p.CompletedRevenue))
            .ToList();
    }

    public async Task<DashboardAttentionCounts> GetAttentionCountsAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default)
    {
        var conversations = await _db.Conversations.CountAsync(c => c.ClientId == clientId
            && c.Status == ConversationStatus.Active
            && (c.Mode == ConversationMode.Human || c.Mode == ConversationMode.Approval), ct);

        var overdueFollowups = await _db.Contacts.CountAsync(l => l.ClientId == clientId
            && l.NextFollowupAt != null && l.NextFollowupAt < now
            // Contacts that are closed (lost) or already converted need no follow-up, whatever their type calls those stages.
            && !_db.PipelineStages.Any(s => s.BusinessTypeCode == l.Client!.BusinessTypeCode && s.Key == l.StageKey
                && (s.Category == StageCategory.ClosedLost || s.Category == StageCategory.Converted)), ct);

        // Same rule as the Bookings calendar's "needs an outcome" notice.
        var needsOutcome = await _db.Bookings.CountAsync(a => a.ClientId == clientId && a.ScheduledStart < now
            && (a.Status == BookingStatus.Booked || a.Status == BookingStatus.Confirmed), ct);

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
