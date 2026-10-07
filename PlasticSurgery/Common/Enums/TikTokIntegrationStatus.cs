namespace PlasticSurgery.Common.Enums;

public static class TikTokIntegrationStatus
{
    public const string Disconnected = "disconnected";
    /// <summary>Redirected to TikTok's consent screen; waiting for the OAuth callback.</summary>
    public const string Pending = "pending";
    public const string Connected = "connected";
    public const string Error = "error";
}
