using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Notifications;

public interface INotificationRepository
{
    void Add(Notification notification);

    /// <summary>Tracked.</summary>
    Task<Notification?> GetAsync(Guid clientId, Guid notificationId, CancellationToken ct = default);

    /// <summary>A page of the client's notifications, newest first (read-only), plus the total and unread counts.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount, int UnreadCount)> ListAsync(Guid clientId, int skip, int take,
        CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Marks every unread notification of the client read, directly in the database.</summary>
    Task MarkAllReadAsync(Guid clientId, DateTimeOffset readAt, CancellationToken ct = default);
}
