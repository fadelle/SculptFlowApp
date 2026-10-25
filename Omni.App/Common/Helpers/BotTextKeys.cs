using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>The text keys the Bots and Inbox Settings pages show for bot vocabulary (templates, statuses, actions, handoff
/// policies, languages, why no bot answers). Each key is a literal here so the UI text guard sees it.</summary>
public static class BotTextKeys
{
    private const string SupportNameKey = "bots.templates.support.name";
    private const string BookingNameKey = "bots.templates.booking.name";
    private const string SalesNameKey = "bots.templates.sales.name";
    private const string CustomNameKey = "bots.templates.custom.name";
    private const string SupportTextKey = "bots.templates.support.text";
    private const string BookingTextKey = "bots.templates.booking.text";
    private const string SalesTextKey = "bots.templates.sales.text";
    private const string CustomTextKey = "bots.templates.custom.text";

    private const string DraftKey = "bots.status.draft";
    private const string ActiveKey = "bots.status.active";
    private const string InactiveKey = "bots.status.inactive";

    private const string KnowledgeKey = "bots.capabilities.knowledge";
    private const string ServicesKey = "bots.capabilities.services";
    private const string AvailabilityKey = "bots.capabilities.availability";
    private const string BookingsKey = "bots.capabilities.bookings";
    private const string ContactUpdateKey = "bots.capabilities.contactUpdate";
    private const string PipelineKey = "bots.capabilities.pipeline";

    private const string OnRequestKey = "bots.handoff.onRequest";
    private const string OnRequestOrUnsureKey = "bots.handoff.onRequestOrUnsure";

    private const string EnglishKey = "bots.languages.en";
    private const string ArabicKey = "bots.languages.ar";
    private const string FrenchKey = "bots.languages.fr";
    private const string SpanishKey = "bots.languages.es";
    private const string GermanKey = "bots.languages.de";
    private const string ItalianKey = "bots.languages.it";
    private const string PortugueseKey = "bots.languages.pt";
    private const string TurkishKey = "bots.languages.tr";
    private const string RussianKey = "bots.languages.ru";
    private const string PersianKey = "bots.languages.fa";
    private const string ArmenianKey = "bots.languages.hy";
    private const string ChineseKey = "bots.languages.zh";

    private const string PlanWithoutAiKey = "inboxSettings.blocked.planWithoutAi";
    private const string AiDisabledKey = "inboxSettings.blocked.aiDisabled";
    private const string NoBotKey = "inboxSettings.blocked.noBot";
    private const string BotInactiveKey = "inboxSettings.blocked.botInactive";
    private const string BotLimitKey = "inboxSettings.blocked.botLimit";

    public static string TemplateName(string template) => template switch
    {
        BotTemplate.Support => SupportNameKey,
        BotTemplate.Booking => BookingNameKey,
        BotTemplate.Sales => SalesNameKey,
        _ => CustomNameKey
    };

    public static string TemplateText(string template) => template switch
    {
        BotTemplate.Support => SupportTextKey,
        BotTemplate.Booking => BookingTextKey,
        BotTemplate.Sales => SalesTextKey,
        _ => CustomTextKey
    };

    public static string Status(string status) => status switch
    {
        BotStatus.Active => ActiveKey,
        BotStatus.Inactive => InactiveKey,
        _ => DraftKey
    };

    /// <summary>The badge colour of a status (UI_GUIDE status tones: green success, gray inactive, amber waiting).</summary>
    public static string StatusBadge(string status) => status switch
    {
        BotStatus.Active => "badge-green",
        BotStatus.Inactive => "badge-gray",
        _ => "badge-amber"
    };

    public static string Capability(string capability) => capability switch
    {
        BotCapability.Knowledge => KnowledgeKey,
        BotCapability.Services => ServicesKey,
        BotCapability.Availability => AvailabilityKey,
        BotCapability.Bookings => BookingsKey,
        BotCapability.ContactUpdate => ContactUpdateKey,
        _ => PipelineKey
    };

    public static string HandoffPolicy(string policy) => policy == BotHandoffPolicy.OnRequest ? OnRequestKey : OnRequestOrUnsureKey;

    public static string Language(string code) => code switch
    {
        "ar" => ArabicKey,
        "fr" => FrenchKey,
        "es" => SpanishKey,
        "de" => GermanKey,
        "it" => ItalianKey,
        "pt" => PortugueseKey,
        "tr" => TurkishKey,
        "ru" => RussianKey,
        "fa" => PersianKey,
        "hy" => ArmenianKey,
        "zh" => ChineseKey,
        _ => EnglishKey
    };

    /// <summary>Why the team answers an account (AiBotBlockReason), or null when its bot does.</summary>
    public static string? BlockReason(string? reason) => reason switch
    {
        null => null,
        AiBotBlockReason.PlanWithoutAi => PlanWithoutAiKey,
        AiBotBlockReason.NoBot => NoBotKey,
        AiBotBlockReason.BotInactive => BotInactiveKey,
        AiBotBlockReason.BotLimit => BotLimitKey,
        _ => AiDisabledKey
    };
}
