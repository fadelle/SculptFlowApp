namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for ChannelIntegration.Channel — must match schema.sql's CHECK constraint.</summary>
public static class ChannelType
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";
    public const string Facebook = "facebook";
    public const string Telegram = "telegram";

    public static readonly IReadOnlyList<string> All = new[] { WhatsApp, Instagram, Facebook, Telegram };
}
