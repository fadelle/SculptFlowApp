using Microsoft.AspNetCore.Identity;

namespace Omni.App.Entities.Models;

/// <summary>
/// Everyone who signs in (identity.users): the ASP.NET Core Identity user plus Omni's own columns. Role decides the app
/// (UserRole: admin → admin portal only; client / member → main app), and is re-checked on every request with IsActive
/// and the user's active client_users row. Named AppUser so it never clashes with ControllerBase.User. Contacts never
/// sign in.
/// </summary>
public class AppUser : IdentityUser
{
    /// <summary>UserRole value.</summary>
    public string Role { get; set; } = string.Empty;

    public string? FullName { get; set; }

    /// <summary>Preferred dashboard language (AppLanguage); null = the client's default language.</summary>
    public string? Language { get; set; }

    /// <summary>False = can't sign in anywhere (an admin deactivated in the portal, a retired account).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
