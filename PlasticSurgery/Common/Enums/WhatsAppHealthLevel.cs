namespace PlasticSurgery.Common.Enums;

/// <summary>Simplified health states shown in the dashboard — derived from the raw account/phone
/// status fields on ChannelIntegration, never Meta's raw vocabulary directly.</summary>
public static class WhatsAppHealthLevel
{
    public const string Healthy = "healthy";
    public const string Warning = "warning";
    public const string Problem = "problem";
    public const string Disconnected = "disconnected";
    public const string Unknown = "unknown";
}
