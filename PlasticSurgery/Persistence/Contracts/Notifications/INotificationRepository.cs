using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Notifications;

public interface INotificationRepository
{
    void Add(Notification notification);

    /// <summary>Tracked.</summary>
    Task<Notification?> GetAsync(Guid clinicId, Guid notificationId, CancellationToken ct = default);

    /// <summary>A page of the clinic's notifications, newest first (read-only), plus the total and unread counts.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount, int UnreadCount)> ListAsync(Guid clinicId, int skip, int take,
        CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Marks every unread notification of the clinic read, directly in the database.</summary>
    Task MarkAllReadAsync(Guid clinicId, DateTimeOffset readAt, CancellationToken ct = default);
}
