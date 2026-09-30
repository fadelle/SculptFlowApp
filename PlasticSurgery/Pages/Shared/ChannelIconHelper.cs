namespace PlasticSurgery.Pages.Shared;

/// <summary>
/// Small colored channel badge (initials + brand-ish color) shown next to conversations in the
/// Inbox — same visual language as the channel_mark badges on Settings → Channels &amp; Integrations,
/// so a WhatsApp conversation always reads the same way across the app. Conversations span
/// multiple channels (WhatsApp today, Instagram/Facebook as those come online), so the list needs
/// an at-a-glance way to tell them apart.
/// </summary>
public static class ChannelIconHelper
{
    private static readonly Dictionary<string, (string Initials, string Hex)> Channels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whatsapp"] = ("WA", "#25D366"),
        ["instagram"] = ("IG", "#C13584"),
        ["facebook"] = ("FB", "#1877F2"),
        ["telegram"] = ("TG", "#229ED9"),
        ["tiktok"] = ("TT", "#000000"),
        ["website"] = ("WEB", "#6b7280"),
        ["sms"] = ("SMS", "#6b7280"),
        ["email"] = ("MAIL", "#6b7280"),
    };

    public static string Initials(string? channel) =>
        channel is not null && Channels.TryGetValue(channel, out var c) ? c.Initials : "?";

    public static string Hex(string? channel) =>
        channel is not null && Channels.TryGetValue(channel, out var c) ? c.Hex : "#9ca3af";

    /// <summary>Recognizable brand glyph (currently just WhatsApp) instead of plain initials —
    /// null for channels that don't have one yet, so the caller falls back to Initials/Hex.</summary>
    public static string? Svg(string? channel) => channel?.ToLowerInvariant() switch
    {
        "whatsapp" => WhatsAppSvg,
        "telegram" => TelegramSvg,
        _ => null
    };

    /// <summary>Brand glyph for every connectable channel (Settings → Channels &amp; Integrations cards).
    /// Kept separate from <see cref="Svg"/> so the Inbox/Leads badges don't change.</summary>
    public static string? BrandSvg(string? channel) => channel?.ToLowerInvariant() switch
    {
        "instagram" => InstagramSvg,
        "facebook" => MessengerSvg,
        "google" => GoogleCalendarSvg,
        "outlook" => OutlookSvg,
        "tiktok" => TikTokSvg,
        _ => Svg(channel)
    };

    private const string TikTokSvg =
        """<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M16.6 5.82c-.86-.78-1.39-1.87-1.39-3.07h-3.3v13.5c0 1.5-1.22 2.72-2.72 2.72s-2.72-1.22-2.72-2.72 1.22-2.72 2.72-2.72c.28 0 .55.04.8.12v-3.35a6.1 6.1 0 0 0-.8-.05c-3.34 0-6.05 2.71-6.05 6.05S6.15 22.3 9.49 22.3s6.05-2.71 6.05-6.05V9.01a7.3 7.3 0 0 0 4.26 1.37V7.08a4.4 4.4 0 0 1-3.2-1.26z"/></svg>""";

    private const string GoogleCalendarSvg =
        """<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><rect x="3" y="3" width="18" height="18" rx="3" fill="#fff"/><path d="M6 3h12a3 3 0 0 1 3 3v2.5H3V6a3 3 0 0 1 3-3z" fill="#4285F4"/><path d="M3 17.5h4.5V21H6a3 3 0 0 1-3-3z" fill="#34A853"/><path d="M16.5 17.5H21V18a3 3 0 0 1-3 3h-1.5z" fill="#FBBC04"/><rect x="3" y="3" width="18" height="18" rx="3" fill="none" stroke="#4285F4" stroke-width="1.2"/><text x="12" y="16.6" text-anchor="middle" font-family="Arial, sans-serif" font-size="7.5" font-weight="700" fill="#4285F4">31</text></svg>""";

    private const string OutlookSvg =
        """<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><rect x="9" y="6" width="12.5" height="12" rx="1.5" stroke="#fff" stroke-width="1.6"/><path d="M9.5 7.5 15.25 12 21 7.5" stroke="#fff" stroke-width="1.6"/><rect x="2.5" y="4" width="10" height="16" rx="1.8" fill="#fff"/><ellipse cx="7.5" cy="12" rx="2.4" ry="3.1" stroke="#0078D4" stroke-width="1.8"/></svg>""";

    private const string InstagramSvg =
        """<svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" xmlns="http://www.w3.org/2000/svg"><rect x="3" y="3" width="18" height="18" rx="5"/><circle cx="12" cy="12" r="4.2"/><circle cx="17.4" cy="6.6" r="1.1" fill="#fff" stroke="none"/></svg>""";

    private const string MessengerSvg =
        """<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M12 2C6.36 2 2 6.13 2 11.7c0 2.91 1.19 5.44 3.14 7.17.16.14.26.35.27.57l.05 1.78a.8.8 0 0 0 1.12.71l1.98-.87c.17-.08.36-.09.53-.04.91.25 1.87.38 2.91.38 5.64 0 10-4.13 10-9.7S17.64 2 12 2zm6 7.46-2.94 4.66a1.5 1.5 0 0 1-2.17.4l-2.34-1.75a.6.6 0 0 0-.72 0l-3.16 2.4c-.42.32-.97-.18-.69-.63l2.94-4.66a1.5 1.5 0 0 1 2.17-.4l2.34 1.75a.6.6 0 0 0 .72 0l3.16-2.4c.42-.32.97.18.69.63z"/></svg>""";

    private const string TelegramSvg =
        """<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M9.78 18.65l.28-4.23 7.68-6.92c.34-.31-.07-.46-.52-.19L7.74 13.3 3.64 12c-.88-.25-.89-.86.2-1.3l15.97-6.16c.73-.33 1.43.18 1.15 1.3l-2.72 12.81c-.19.91-.74 1.13-1.5.71L12.6 16.3l-1.99 1.93c-.23.23-.42.42-.83.42z"/></svg>""";

    private const string WhatsAppSvg =
        """<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 0 1-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 0 1-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 0 1 2.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0 0 12.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 0 0 5.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 0 0-3.48-8.413"/></svg>""";
}
