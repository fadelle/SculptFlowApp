namespace PlasticSurgery.Common.Configs;

/// <summary>"Continue with Google" for signing in / signing up to SculptFlow itself (GoogleLogin:* config) —
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

    /// <summary>Fixed, user-safe wording for the externalError codes the sign-in page can receive — the page
    /// never echoes arbitrary text from the query string.</summary>
    public static string? ErrorMessage(string? code) => code switch
    {
        "cancelled" => "Google sign-in was cancelled.",
        "failed" => "Google sign-in didn't work. Please try again.",
        "unverified" => "Google couldn't confirm that email address, so we can't sign you in with it.",
        "locked" => "Too many attempts. For your security, wait a few minutes and try again.",
        _ => null
    };
}
