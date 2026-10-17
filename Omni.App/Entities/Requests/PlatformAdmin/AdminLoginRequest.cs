namespace Omni.App.Entities.Requests.PlatformAdmin;

/// <summary>Body for POST /api/platform-admin/auth/login: what the admin typed on the portal's sign-in page.</summary>
public record AdminLoginRequest(string? Email, string? Password);
