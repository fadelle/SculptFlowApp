namespace Omni.App.Common.Enums;

/// <summary>What a pipeline stage means, whatever a business type calls it (crm.pipeline_stages.category; must match the
/// check constraint in Database/schema.sql). Code, reports and the AI work with categories; every business type has at
/// least one stage in each.</summary>
public static class StageCategory
{
    public const string Open = "open";
    public const string Booked = "booked";
    public const string Attended = "attended";
    public const string NoShow = "no_show";
    public const string Converted = "converted";
    public const string ClosedLost = "closed_lost";
    public const string NeedsHuman = "needs_human";

    /// <summary>In pipeline order.</summary>
    public static readonly IReadOnlyList<string> All = [Open, Booked, Attended, NoShow, Converted, ClosedLost, NeedsHuman];
}
