namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for ChannelIntegration.Status — must match schema.sql's CHECK constraint.</summary>
public static class ChannelIntegrationStatus
{
    public const string Disconnected = "disconnected";
    public const string Connected = "connected";
    public const string Error = "error";
}
