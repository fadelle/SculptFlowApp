namespace PlasticSurgery.Common.Enums;

public static class ConversationChannel
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";
    public const string Website = "website";
    public const string Facebook = "facebook";
    public const string Sms = "sms";
    public const string Email = "email";
    public const string Telegram = "telegram";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        WhatsApp, Instagram, Website, Facebook, Sms, Email, Telegram
    };
}
