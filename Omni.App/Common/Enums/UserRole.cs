namespace Omni.App.Common.Enums;

/// <summary>Allowed values for identity.users.role (must match ck_users_role in Database/schema.sql). The role decides
/// which app a user may sign in to; it is checked at sign-in and again on every request.</summary>
public static class UserRole
{
    /// <summary>Platform operator: the admin portal only (signs in through /api/platform-admin/auth/login). No client.</summary>
    public const string Admin = "admin";
    /// <summary>The person who signed a client up: the main app, including managing the client's members.</summary>
    public const string Client = "client";
    /// <summary>Added by a client user: the main app, everything except managing members.</summary>
    public const string Member = "member";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Admin, Client, Member };

    /// <summary>Roles that may use the main app (the client dashboard).</summary>
    public static bool UsesMainApp(string? role) => role is Client or Member;
}
