namespace Omni.App.Business.Contracts.Services.Accounts;

/// <summary>
/// "Forgot password?" for client and member users: a one-time link by email (identity.account_tokens, purpose
/// password_reset), valid Accounts:ResetLinkMinutes, at most Accounts:ResetEmailsPerHour emails per account per hour.
/// Admin users reset their password in the admin portal, never here. Links point at App:PublicBaseUrl when it is set,
/// else at the address the page was opened on.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>Sends a link when the email belongs to an active client or member user and the hourly limit allows it;
    /// otherwise does nothing. The caller shows the same answer either way, so nobody learns whether an account exists.
    /// A provider failure is logged, not thrown, for the same reason.</summary>
    Task RequestAsync(string? email, string requestBaseUrl, string? pageLanguage, CancellationToken ct = default);

    /// <summary>Whether the link's token can still be used (not used, revoked or expired, and its user can sign in).</summary>
    Task<bool> IsUsableAsync(string? token, CancellationToken ct = default);

    /// <summary>Sets the new password, clears the lockout, renews the security stamp (every other session is signed out)
    /// and closes every open reset link of the user. Throws ArgumentException (resetPassword.errors.missingFields /
    /// .mismatch, or Identity's password rules) and InvalidOperationException (resetPassword.errors.linkInvalid).</summary>
    Task ResetAsync(string? token, string? password, string? confirmPassword, CancellationToken ct = default);

    /// <summary>The admin portal's "send a password reset email" for a client or member user (no hourly limit: an operator
    /// asked). Throws KeyNotFoundException for an unknown or admin user and EmailSendException when the email can't go.</summary>
    Task SendForUserAsync(string userId, string requestBaseUrl, CancellationToken ct = default);
}
