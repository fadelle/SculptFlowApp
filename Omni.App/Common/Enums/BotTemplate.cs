namespace Omni.App.Common.Enums;

/// <summary>crm.bots.template: what a bot was made from. A template bot (support, booking, sales) runs the template's locked
/// core instructions and actions (Common/Statics/BotTemplates); a custom bot runs its own instructions and allowed actions.</summary>
public static class BotTemplate
{
    /// <summary>Answers questions from the Knowledge Base and hands off when needed.</summary>
    public const string Support = "support";

    /// <summary>Answers questions, checks availability and books, moves and cancels bookings.</summary>
    public const string Booking = "booking";

    /// <summary>Qualifies contacts, collects their details and moves them through the pipeline.</summary>
    public const string Sales = "sales";

    /// <summary>The client's own instructions and allowed actions.</summary>
    public const string Custom = "custom";

    /// <summary>In the order the "New bot" popup offers them.</summary>
    public static readonly IReadOnlyList<string> All = [Support, Booking, Sales, Custom];
}
