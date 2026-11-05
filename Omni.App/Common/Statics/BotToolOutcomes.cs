namespace Omni.App.Common.Statics;

/// <summary>
/// The business outcomes of an AI tool call that Omni refuses before the tool runs (HTTP 200, success = false, like the
/// tools' other business outcomes), each with an instruction the AI follows. One wording for the /api/ai filters
/// (BotToolAttribute, RequireFeatureAttribute) and IBotToolService.CallAsync.
/// </summary>
public static class BotToolOutcomes
{
    public const string NotAllowedCode = "TOOL_NOT_ALLOWED";
    public const string FeatureNotIncludedCode = "FEATURE_NOT_INCLUDED";

    /// <summary>The bot may not use this tool (IBotRuntimeService.AuthorizeAsync refused it; BotToolCheck reasons).</summary>
    public static object NotAllowed(string? reason) => new
    {
        success = false,
        code = NotAllowedCode,
        reason,
        message = "This bot isn't allowed to use this tool.", // text-ok: read by the AI, never shown to the contact
        instruction = "Nothing was looked up, booked or changed. Don't offer or attempt this again. Tell the contact you can't " + // text-ok: AI instruction
            "help with that here and that a team member can, following your handoff policy." // text-ok: AI instruction
    };

    public const string BotNotAllowedCode = "BOT_NOT_ALLOWED";

    /// <summary>The bot itself may no longer answer (deactivated, deleted, beyond the plan's bots, the plan lost the AI
    /// assistant; BotToolCheck reasons): answered as a 403, the run stops and the conversation goes to the team (QA t7-01).</summary>
    public static object BotNotAllowed(string? reason) => new
    {
        success = false,
        code = BotNotAllowedCode,
        reason,
        message = "This bot may no longer answer this conversation.", // text-ok: read by the AI, never shown to the contact
        instruction = "Stop. Nothing was sent, looked up, booked or changed. A team member will take over." // text-ok: AI instruction
    };

    /// <summary>The client's plan doesn't include the feature the tool needs (an EntitlementKeys value).</summary>
    public static object FeatureNotIncluded(string feature) => new
    {
        success = false,
        code = FeatureNotIncludedCode,
        feature,
        message = $"The business's plan doesn't include {feature}.", // text-ok: read by the AI, never shown to the contact
        instruction = "Nothing was booked, changed or looked up. Do not offer, check or confirm bookings or available times. "
            + "Tell the contact a team member will help them with that, and call handoff_to_human." // text-ok: AI instruction
    };
}
