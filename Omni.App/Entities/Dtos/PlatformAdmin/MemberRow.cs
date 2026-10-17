namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A client or member user with their (active first) membership, for the admin portal. Role is client or member;
/// UserActive is identity.users.is_active (MembershipActive is the client_users row).</summary>
public record MemberRow(string UserId, string? Email, string? FullName, Guid? MembershipId, Guid? ClientId, string? ClientName,
    bool MembershipActive, bool LoginLocked, bool EmailConfirmed, int AccessFailedCount, bool HasPassword, bool HasGoogleLogin,
    DateTimeOffset? JoinedAt, string Role, bool UserActive, DateTimeOffset? LastLoginAt);
