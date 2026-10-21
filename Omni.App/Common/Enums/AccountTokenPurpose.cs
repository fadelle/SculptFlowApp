namespace Omni.App.Common.Enums;

/// <summary>Allowed values for identity.account_tokens.purpose (must match ck_account_tokens_purpose in Database/schema.sql).</summary>
public static class AccountTokenPurpose
{
    /// <summary>A client user invited someone to become a member (S2).</summary>
    public const string Invite = "invite";
    /// <summary>"Forgot password?" or the admin portal's reset email.</summary>
    public const string PasswordReset = "password_reset";
}
