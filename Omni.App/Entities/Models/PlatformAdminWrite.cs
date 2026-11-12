namespace Omni.App.Entities.Models;

/// <summary>One write to the platform-admin API (activity.platform_admin_writes; QA t6-admin-23), recorded by the main app
/// itself: the portal's own audit log only sees calls made through the portal, and anyone holding the shared key can call
/// the API directly. <see cref="Actor"/> is the X-Admin-Actor header as sent (free text, so read it with the address).</summary>
public class PlatformAdminWrite
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public Guid? ClientId { get; set; }
}
