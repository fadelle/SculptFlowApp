namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Who an admin is: the answer of POST /api/platform-admin/auth/login and GET .../auth/admins/{id}. The portal puts
/// these in its own cookie. SessionStamp is a fingerprint of the account's security stamp (never the stamp itself): it changes when
/// the password is set, so the portal signs that admin's open sessions out (QA t6-admin-13).</summary>
public record AdminIdentity(string Id, string? FullName, string? Email, string? SessionStamp = null);
