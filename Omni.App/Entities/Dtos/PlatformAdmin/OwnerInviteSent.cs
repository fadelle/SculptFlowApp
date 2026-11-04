namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A client's owner invite went out again (POST /api/platform-admin/clients/{id}/owner-invite).</summary>
public record OwnerInviteSent(Guid ClientId, string Email, DateTimeOffset ExpiresAt);
