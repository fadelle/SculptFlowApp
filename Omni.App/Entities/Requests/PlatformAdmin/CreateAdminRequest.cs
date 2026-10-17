namespace Omni.App.Entities.Requests.PlatformAdmin;

/// <summary>Body for POST /api/platform-admin/admins: a new admin user.</summary>
public record CreateAdminRequest(string? Email, string? FullName, string? Password);
