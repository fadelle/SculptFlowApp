using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Notifications;
using PlasticSurgery.Business.Mappers.Notifications;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Notifications;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Notifications;

namespace PlasticSurgery.Business.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxNotifier _notifier;

    public NotificationService(INotificationRepository notifications, IUnitOfWork unitOfWork, IInboxNotifier notifier)
    {
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
    }

    public async Task CreateAsync(
        Guid clinicId, string type, string title, string? message,
        Guid? leadId = null, Guid? conversationId = null, Guid? appointmentId = null, Guid? channelIntegrationId = null,
        string? link = null, CancellationToken ct = default)
    {
        try
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                Type = type,
                Title = title,
                Message = message,
                LeadId = leadId,
                ConversationId = conversationId,
                AppointmentId = appointmentId,
                ChannelIntegrationId = channelIntegrationId,
                Link = link,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _notifications.Add(notification);
            await _unitOfWork.SaveChangesAsync(ct);

            await _notifier.NotificationCreatedAsync(clinicId, NotificationMapper.ToResponse(notification), ct);
        }
        catch
        {
            // Never let a notification problem fail or roll back the real operation it's attached to — the
            // appointment/message/handoff/etc. it describes has already succeeded and stays succeeded either way.
        }
    }

    public async Task<NotificationListResponse> ListAsync(Guid clinicId, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount, unreadCount) = await _notifications.ListAsync(clinicId, skip, take, ct);
        return new NotificationListResponse(items.Select(NotificationMapper.ToResponse).ToList(), unreadCount, totalCount);
    }

    public Task<int> GetUnreadCountAsync(Guid clinicId, CancellationToken ct = default) =>
        _notifications.CountUnreadAsync(clinicId, ct);

    public async Task MarkReadAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var notification = await _notifications.GetAsync(clinicId, id, ct);
        if (notification is null || notification.IsRead) return;

        notification.IsRead = true;
        notification.ReadAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task MarkAllReadAsync(Guid clinicId, CancellationToken ct = default) =>
        _notifications.MarkAllReadAsync(clinicId, DateTimeOffset.UtcNow, ct);
}
