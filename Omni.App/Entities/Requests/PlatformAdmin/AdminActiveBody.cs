namespace Omni.App.Entities.Requests.PlatformAdmin;

/// <summary>Body for PUT /api/platform-admin/admins/{id}/active. ActingAdminId is the admin doing it (the portal's signed-in
/// admin), so nobody deactivates their own account.</summary>
public record AdminActiveBody(bool Active, string? ActingAdminId);
