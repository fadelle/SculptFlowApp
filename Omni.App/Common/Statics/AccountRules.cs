namespace Omni.App.Common.Statics;

/// <summary>Fixed sign-in and password rules for identity.users (security policy, not tunable settings). The lockout
/// matches what the admin portal enforced for its own admins before they moved into identity.users.</summary>
public static class AccountRules
{
    /// <summary>Failed sign-ins in a row that lock an account (Identity lockout).</summary>
    public const int MaxFailedSignIns = 5;

    /// <summary>How long a locked account stays locked.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Minimum password length for admin users (on top of Identity's password rules for everyone).</summary>
    public const int AdminMinPasswordLength = 10;
}
