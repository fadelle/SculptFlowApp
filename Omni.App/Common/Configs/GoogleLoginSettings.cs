namespace Omni.App.Common.Configs;

/// <summary>"Continue with Google" for signing in / signing up to Omni itself (GoogleLogin:* config) —
/// unrelated to Calendar Integrations, which has its own Google client and scopes (GoogleCalendar:*). Login
/// requests only openid/email/profile.</summary>
public static class GoogleLoginSettings
{
    public const string Provider = "Google";

    /// <summary>Claim carrying Google's own "this email is verified" flag (mapped in Program.cs).</summary>
    public const string EmailVerifiedClaim = "urn:google:email_verified";

    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["GoogleLogin:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["GoogleLogin:ClientSecret"]);

    private const string CancelledKey = "auth.google.errors.cancelled";
    private const string FailedKey = "auth.google.errors.failed";
    private const string UnverifiedKey = "auth.google.errors.unverified";
    private const string LockedKey = "login.errors.lockedOut";
    private const string SignupClosedKey = "auth.google.errors.signupClosed";

    /// <summary>The UI text key (en.json) for an externalError code the sign-in page can receive: fixed, user-safe
    /// wording, so the page never echoes arbitrary text from the query string. Null for an unknown code.</summary>
    public static string? ErrorKey(string? code) => code switch
    {
        "cancelled" => CancelledKey,
        "failed" => FailedKey,
        "unverified" => UnverifiedKey,
        "locked" => LockedKey,
        "signup_closed" => SignupClosedKey,
        _ => null
    };
}
