namespace Omni.App.Entities.Dtos.Clients;

/// <summary>What a signed-in main-app user may use right now: an active client or member user with an active
/// membership. Read (cached) on every signed-in request by CurrentClientContext; there is none for an admin, a
/// deactivated user or a user without an active membership. SecurityStamp lets the cookie check sign the user out
/// everywhere after a password change or a lock.</summary>
public record UserAccess(string UserId, Guid ClientId, string Role, string? Language, string? FullName, string? SecurityStamp);
