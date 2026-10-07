namespace PlasticSurgery.Common.Enums;

/// <summary>Closed set (DB CHECK ck_campaigns_channel) — mirrors channel_integrations.channel.
/// Only WhatsApp is actually implemented; the others are reserved.</summary>
public static class CampaignChannel
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";
    public const string Messenger = "messenger";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { WhatsApp, Instagram, Messenger };
}
