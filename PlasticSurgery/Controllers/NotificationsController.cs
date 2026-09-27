using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>The notification bell's API — list/unread-count/mark-read, all clinic-scoped via CurrentClinicContext
/// like every other dashboard controller (DashboardApiController). See INotificationService for when each
/// notification type is created.</summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : DashboardApiController
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationListResponse>> List([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        take = Math.Clamp(take, 1, 100);
        return Ok(await _notifications.ListAsync(clinicId.Value, skip, take, ct));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        return Ok(new { count = await _notifications.GetUnreadCountAsync(clinicId.Value, ct) });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        await _notifications.MarkReadAsync(clinicId.Value, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        await _notifications.MarkAllReadAsync(clinicId.Value, ct);
        return NoContent();
    }
}
