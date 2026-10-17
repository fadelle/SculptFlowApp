namespace Omni.App.Common.Helpers;

/// <summary>
/// Maps free-text status values (qualification, booking status, campaign status…) to a client-friendly display label.
/// Contact stages are not here: their labels come from the client's business type (StageDisplay); a stage key the type
/// doesn't know falls back to the mechanical label below. Purely presentational — the
/// underlying stored/backend values (Database/schema.sql's check constraints, the *Status static
/// classes in Data/Entities) are unchanged; this only controls what client members read on screen.
/// </summary>
public static class FilterLabelHelper
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        // qualifications
        ["unknown"] = "Unknown",
        ["hot"] = "Hot",
        ["warm"] = "Warm",
        ["cold"] = "Cold",
        ["needs_human"] = "Needs human review",
        ["spam"] = "Spam",

        // booking statuses
        ["booked"] = "Booked",
        ["confirmed"] = "Confirmed",
        ["attended"] = "Attended",
        ["no_show"] = "No-show",
        ["canceled"] = "Canceled",
        ["rescheduled"] = "Rescheduled",
    };

    /// <summary>Falls back to a mechanical "replace underscores, capitalize first letter" for any
    /// value not explicitly mapped above (e.g. a status added later and not yet given a bespoke
    /// label) rather than showing the raw snake_case value.</summary>
    public static string Label(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        if (Labels.TryGetValue(value, out var label)) return label;

        var spaced = value.Replace('_', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
