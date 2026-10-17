namespace Omni.App.Common.Statics;

/// <summary>Claim types the main app puts on a signed-in user's cookie (AppUserClaimsPrincipalFactory), read from
/// identity.users at sign-in. The role and membership are re-checked on every request, so these are for display only.</summary>
public static class MemberClaims
{
    /// <summary>The user's full name (users.full_name).</summary>
    public const string FullName = "full_name";

    /// <summary>The user's role (users.role: client or member in the main app).</summary>
    public const string Role = "omni_role";
}
