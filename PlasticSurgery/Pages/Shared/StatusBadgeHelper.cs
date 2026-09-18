namespace PlasticSurgery.Pages.Shared;

/// <summary>
/// Maps the free-text status columns (leads.status, appointments.status, etc.) to a badge color
/// class for the dashboard UI. Purely presentational — the underlying values are still whatever
/// Database/schema.sql's check constraints allow.
/// </summary>
public static class StatusBadgeHelper
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        // lead statuses
        ["new"] = "badge-blue",
        ["contacted"] = "badge-amber",
        ["qualified"] = "badge-purple",
        ["consultation_booked"] = "badge-amber",
        ["consultation_attended"] = "badge-green",
        ["no_show"] = "badge-red",
        ["surgery_booked"] = "badge-teal",
        ["not_interested"] = "badge-gray",
        ["needs_human"] = "badge-red",
        ["lost"] = "badge-gray",

        // qualification statuses
        ["hot"] = "badge-red",
        ["warm"] = "badge-amber",
        ["cold"] = "badge-blue",
        ["unknown"] = "badge-gray",
        ["medical_question"] = "badge-purple",
        ["spam"] = "badge-gray",

        // appointment statuses
        ["booked"] = "badge-amber",
        ["confirmed"] = "badge-blue",
        ["attended"] = "badge-green",
        ["canceled"] = "badge-gray",
        ["rescheduled"] = "badge-purple",

        // conversation mode (Inbox) — "human" is the same red as "needs_human" intentionally:
        // both mean "a person needs to look at this, AI is not handling it".
        ["ai"] = "badge-teal",
        ["human"] = "badge-red",
        ["approval"] = "badge-purple",

        // conversation status (Inbox)
        ["active"] = "badge-green",
        ["closed"] = "badge-gray",
        ["archived"] = "badge-gray",

        // WhatsApp template review status
        ["draft"] = "badge-gray",
        ["pending"] = "badge-amber",
        ["approved"] = "badge-green",
        ["rejected"] = "badge-red",
        ["paused"] = "badge-amber",
        ["disabled"] = "badge-gray",
        ["flagged"] = "badge-red",
        ["deleted"] = "badge-gray",

        // WhatsApp connection/account health level (see ChannelIntegration.HealthLevel)
        ["healthy"] = "badge-green",
        ["warning"] = "badge-amber",
        ["problem"] = "badge-red",
        ["disconnected"] = "badge-gray",

        // campaign status
        ["scheduled"] = "badge-amber",
        ["running"] = "badge-teal",
        ["completed"] = "badge-green",
        ["cancelled"] = "badge-gray",
        ["failed"] = "badge-red",

        // campaign recipient status
        ["queued"] = "badge-amber",
        ["sent"] = "badge-blue",
        ["delivered"] = "badge-teal",
        ["read"] = "badge-green",
    };

    public static string CssClass(string? status) =>
        status is not null && Colors.TryGetValue(status, out var cls) ? cls : "badge-gray";

    private static readonly HashSet<string> KnownTemplateStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft", "pending", "approved", "rejected", "paused", "disabled"
    };

    /// <summary>Simplified label for a WhatsApp template status — anything Meta sends that isn't
    /// one of our known six (e.g. "flagged", "in_appeal", a status Meta introduces later) is shown
    /// as "Problem" rather than the raw Meta term, per "translate events into simple human-readable
    /// states, don't expose raw Meta complexity to staff".</summary>
    public static string TemplateStatusLabel(string? status)
    {
        if (status is null) return "Unknown";
        if (KnownTemplateStatuses.Contains(status)) return Capitalize(status);
        return "Problem";
    }

    /// <summary>Badge color for a WhatsApp template status — mirrors TemplateStatusLabel's bucketing
    /// (an unrecognized status reads as a problem, so it's colored red, not the generic gray
    /// fallback CssClass would otherwise give an unmapped value).</summary>
    public static string TemplateStatusCssClass(string? status) =>
        status is not null && KnownTemplateStatuses.Contains(status) ? CssClass(status) : "badge-red";

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
