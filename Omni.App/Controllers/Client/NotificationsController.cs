using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Entities.Responses.Notifications;

namespace Omni.App.Controllers.Client;

/// <summary>The notification bell's API — list/unread-count/mark-read, all client-scoped via CurrentClientContext
/// like every other dashboard controller (DashboardApiController). See INotificationService for when each
/// notification type is created.</summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : DashboardApiController
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications, ICurrentClientContext clientContext) : base(clientContext)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationListResponse>> List([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 100);
        return Ok(await _notifications.ListAsync(clientId.Value, skip, take, ct));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(new { count = await _notifications.GetUnreadCountAsync(clientId.Value, ct) });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        await _notifications.MarkReadAsync(clientId.Value, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        await _notifications.MarkAllReadAsync(clientId.Value, ct);
        return NoContent();
    }
}
