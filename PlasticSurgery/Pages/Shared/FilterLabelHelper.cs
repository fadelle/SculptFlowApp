namespace PlasticSurgery.Pages.Shared;

/// <summary>
/// Maps free-text status values (lead status, qualification status, appointment status) to a
/// clinic-friendly display label for the Campaign audience filter UI. Purely presentational — the
/// underlying stored/backend values (Database/schema.sql's check constraints, the *Status static
/// classes in Data/Entities) are unchanged; this only controls what clinic staff read on screen.
/// </summary>
public static class FilterLabelHelper
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        // lead statuses
        ["new"] = "New",
        ["contacted"] = "Contacted",
        ["qualified"] = "Qualified",
        ["consultation_booked"] = "Consultation booked",
        ["consultation_attended"] = "Consultation attended",
        ["no_show"] = "No-show",
        ["surgery_booked"] = "Surgery booked",
        ["not_interested"] = "Not interested",
        ["needs_human"] = "Needs human review",
        ["lost"] = "Lost",

        // qualification statuses
        ["unknown"] = "Unknown",
        ["hot"] = "Hot",
        ["warm"] = "Warm",
        ["cold"] = "Cold",
        ["medical_question"] = "Medical question",
        ["spam"] = "Spam",

        // appointment statuses
        ["booked"] = "Booked",
        ["confirmed"] = "Confirmed",
        ["attended"] = "Attended",
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
