using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IInboxNotifier _notifier;

    public NotificationService(ApplicationDbContext db, IInboxNotifier notifier)
    {
        _db = db;
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
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync(ct);

            await _notifier.NotificationCreatedAsync(clinicId, ToResponse(notification), ct);
        }
        catch
        {
            // Never let a notification problem fail or roll back the real operation it's attached to — the
            // appointment/message/handoff/etc. it describes has already succeeded and stays succeeded either way.
        }
    }

    public async Task<NotificationListResponse> ListAsync(Guid clinicId, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.ClinicId == clinicId);

        var totalCount = await query.CountAsync(ct);
        var unreadCount = await query.CountAsync(n => !n.IsRead, ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip).Take(take)
            .Select(n => ToResponse(n))
            .ToListAsync(ct);

        return new NotificationListResponse(items, unreadCount, totalCount);
    }

    public Task<int> GetUnreadCountAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.ClinicId == clinicId && !n.IsRead, ct);

    public async Task MarkReadAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.ClinicId == clinicId && n.Id == id, ct);
        if (notification is null || notification.IsRead) return;

        notification.IsRead = true;
        notification.ReadAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid clinicId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _db.Notifications
            .Where(n => n.ClinicId == clinicId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now), ct);
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id, n.Type, n.Title, n.Message, n.LeadId, n.ConversationId, n.AppointmentId, n.Link, n.IsRead, n.CreatedAt);
}
