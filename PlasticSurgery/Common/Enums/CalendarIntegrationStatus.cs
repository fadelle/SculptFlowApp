namespace PlasticSurgery.Common.Enums;

public static class CalendarIntegrationStatus
{
    public const string Disconnected = "disconnected";
    /// <summary>Redirected to the provider's consent screen; waiting for the OAuth callback.</summary>
    public const string Pending = "pending";
    public const string Connected = "connected";
    public const string Error = "error";
}
