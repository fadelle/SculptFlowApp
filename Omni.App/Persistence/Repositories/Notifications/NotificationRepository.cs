using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Notifications;

namespace Omni.App.Persistence.Repositories.Notifications;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _db;

    public NotificationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Notification notification) => _db.Notifications.Add(notification);

    public Task<Notification?> GetAsync(Guid clientId, Guid notificationId, CancellationToken ct = default) =>
        _db.Notifications.FirstOrDefaultAsync(n => n.ClientId == clientId && n.Id == notificationId, ct);

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount, int UnreadCount)> ListAsync(Guid clientId, int skip,
        int take, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.ClientId == clientId);

        var totalCount = await query.CountAsync(ct);
        var unreadCount = await query.CountAsync(n => !n.IsRead, ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return (items, totalCount, unreadCount);
    }

    public Task<int> CountUnreadAsync(Guid clientId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.ClientId == clientId && !n.IsRead, ct);

    public Task MarkAllReadAsync(Guid clientId, DateTimeOffset readAt, CancellationToken ct = default) =>
        _db.Notifications
            .Where(n => n.ClientId == clientId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, readAt), ct);
}
