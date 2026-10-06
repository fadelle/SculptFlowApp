using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Notifications;

namespace PlasticSurgery.Persistence.Repositories.Notifications;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _db;

    public NotificationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(Notification notification) => _db.Notifications.Add(notification);

    public Task<Notification?> GetAsync(Guid clinicId, Guid notificationId, CancellationToken ct = default) =>
        _db.Notifications.FirstOrDefaultAsync(n => n.ClinicId == clinicId && n.Id == notificationId, ct);

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount, int UnreadCount)> ListAsync(Guid clinicId, int skip,
        int take, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.ClinicId == clinicId);

        var totalCount = await query.CountAsync(ct);
        var unreadCount = await query.CountAsync(n => !n.IsRead, ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return (items, totalCount, unreadCount);
    }

    public Task<int> CountUnreadAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.ClinicId == clinicId && !n.IsRead, ct);

    public Task MarkAllReadAsync(Guid clinicId, DateTimeOffset readAt, CancellationToken ct = default) =>
        _db.Notifications
            .Where(n => n.ClinicId == clinicId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, readAt), ct);
}
