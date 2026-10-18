using Microsoft.Extensions.Localization;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Maps the free-text status columns (bookings.status, contacts.qualification, etc.) to a badge color
/// class for the dashboard UI. Contact stages are coloured by their category (StageDisplay). Purely presentational — the underlying values are still whatever
/// Database/schema.sql's check constraints allow.
/// </summary>
public static class StatusBadgeHelper
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        // qualifications
        ["hot"] = "badge-red",
        ["warm"] = "badge-amber",
        ["cold"] = "badge-blue",
        ["unknown"] = "badge-gray",
        ["needs_human"] = "badge-red",
        ["spam"] = "badge-gray",

        // booking statuses
        ["booked"] = "badge-amber",
        ["confirmed"] = "badge-blue",
        ["attended"] = "badge-green",
        ["no_show"] = "badge-red",
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
    /// states, don't expose raw Meta complexity to members".</summary>
    public static string TemplateStatusLabel(this IStringLocalizer text, string? status)
    {
        if (status is null) return text[UnknownKey].Value;
        return KnownTemplateStatuses.Contains(status) ? text.ValueLabel(status) : text[ProblemKey].Value;
    }

    /// <summary>Badge color for a WhatsApp template status — mirrors TemplateStatusLabel's bucketing
    /// (an unrecognized status reads as a problem, so it's colored red, not the generic gray
    /// fallback CssClass would otherwise give an unmapped value).</summary>
    public static string TemplateStatusCssClass(string? status) =>
        status is not null && KnownTemplateStatuses.Contains(status) ? CssClass(status) : "badge-red";

    /// <summary>Label for a campaign status. Stored "paused" is what the Stop button sets, so members read it as "Stopped";
    /// every other status uses the shared FilterLabelHelper wording.</summary>
    public static string CampaignStatusLabel(this IStringLocalizer text, string? status) =>
        string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase) ? text[StoppedKey].Value : text.ValueLabel(status);

    private const string UnknownKey = "values.unknown";
    private const string ProblemKey = "values.problem";
    private const string StoppedKey = "campaigns.status.stopped";
}
