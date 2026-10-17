using System.Net.Mail;

namespace Omni.App.Common.Helpers;

/// <summary>The one email-address check for sign-ups, members and admins: a plain address (no display name), at most 256
/// characters (identity.users.email).</summary>
public static class EmailAddress
{
    public static bool IsValid(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > 256) return false;
        try { return new MailAddress(email).Address == email; }
        catch (FormatException) { return false; }
    }
}
