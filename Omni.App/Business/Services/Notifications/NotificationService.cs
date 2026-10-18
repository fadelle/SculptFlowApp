using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Mappers.Notifications;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Notifications;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Notifications;

namespace Omni.App.Business.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxNotifier _notifier;
    private readonly IStringLocalizer _text;

    public NotificationService(INotificationRepository notifications, IUnitOfWork unitOfWork, IInboxNotifier notifier, IStringLocalizer text)
    {
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _text = text;
    }

    public async Task CreateAsync(
        Guid clientId, string type, NotificationText text,
        Guid? contactId = null, Guid? conversationId = null, Guid? bookingId = null, Guid? channelIntegrationId = null,
        string? link = null, CancellationToken ct = default)
    {
        try
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                Type = type,
                Title = text.TitleKey,
                Message = text.MessageKey,
                TextArgs = text.Args.Select(TextArg.Encode).ToArray(),
                ContactId = contactId,
                ConversationId = conversationId,
                BookingId = bookingId,
                ChannelIntegrationId = channelIntegrationId,
                Link = link,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _notifications.Add(notification);
            await _unitOfWork.SaveChangesAsync(ct);

            // Its text is only a preview (often written outside a request, so English): every open bell re-reads the
            // list, in its own language, when this arrives.
            await _notifier.NotificationCreatedAsync(clientId, NotificationMapper.ToResponse(notification, _text), ct);
        }
        catch
        {
            // Never let a notification problem fail or roll back the real operation it's attached to — the
            // booking/message/handoff/etc. it describes has already succeeded and stays succeeded either way.
        }
    }

    public async Task<NotificationListResponse> ListAsync(Guid clientId, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount, unreadCount) = await _notifications.ListAsync(clientId, skip, take, ct);
        return new NotificationListResponse(items.Select(n => NotificationMapper.ToResponse(n, _text)).ToList(), unreadCount, totalCount);
    }

    public Task<int> GetUnreadCountAsync(Guid clientId, CancellationToken ct = default) =>
        _notifications.CountUnreadAsync(clientId, ct);

    public async Task MarkReadAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var notification = await _notifications.GetAsync(clientId, id, ct);
        if (notification is null || notification.IsRead) return;

        notification.IsRead = true;
        notification.ReadAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public Task MarkAllReadAsync(Guid clientId, CancellationToken ct = default) =>
        _notifications.MarkAllReadAsync(clientId, DateTimeOffset.UtcNow, ct);
}
