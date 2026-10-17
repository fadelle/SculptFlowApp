namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Who an admin is: the answer of POST /api/platform-admin/auth/login and GET .../auth/admins/{id}. The portal puts
/// these in its own cookie.</summary>
public record AdminIdentity(string Id, string? FullName, string? Email);
