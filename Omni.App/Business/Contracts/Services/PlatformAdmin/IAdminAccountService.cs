using Omni.App.Entities.Dtos.PlatformAdmin;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// Admin users (identity.users with role <c>admin</c>) for the admin portal, which has no table access: it signs admins
/// in through <see cref="SignInAsync"/>, re-checks them on every request with <see cref="GetActiveAsync"/>, and manages
/// them through the rest. Admins can't use the main app (AppSignInManager refuses the role), and client/member users
/// never pass here. Passwords use Identity's PasswordHasher, the same format the portal's old admin table used.
/// </summary>
public interface IAdminAccountService
{
    /// <summary>Checks an admin's email and password with Identity's lockout (5 failures lock for 15 minutes) and stamps
    /// last_login_at. Null for anything else (unknown email, not an admin, inactive, locked, wrong password), so the
    /// caller answers one generic error and nobody learns which emails exist.</summary>
    Task<AdminIdentity?> SignInAsync(string? email, string? password, CancellationToken ct = default);

    /// <summary>The admin, while still an active admin; null otherwise (the portal then signs them out).</summary>
    Task<AdminIdentity?> GetActiveAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<AdminRow>> ListAsync(CancellationToken ct = default);

    /// <summary>ArgumentException for a missing name, a bad or taken email, or a password shorter than 10 characters
    /// or refused by the password rules.</summary>
    Task<AdminRow> CreateAsync(string? email, string? fullName, string? password, CancellationToken ct = default);

    /// <summary>ArgumentException when an admin deactivates their own account or the last active admin; KeyNotFoundException
    /// for an unknown admin.</summary>
    Task<AdminRow> SetActiveAsync(string id, bool active, string? actingAdminId, CancellationToken ct = default);

    /// <summary>Sets a new password (same rules as <see cref="CreateAsync"/>) and clears the lockout.</summary>
    Task<AdminRow> SetPasswordAsync(string id, string? password, CancellationToken ct = default);
}
