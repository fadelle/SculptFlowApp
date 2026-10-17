namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>An admin user (identity.users with role admin) for the admin portal's Admins page. Never carries the password hash.</summary>
public record AdminRow(string Id, string? Email, string? FullName, bool IsActive, bool LoginLocked, DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);
