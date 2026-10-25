namespace Omni.App.Common.Enums;

/// <summary>Why no AI bot answers on a connected account (AccountAiDecision.BlockReason). Also the reason recorded on the
/// human_handoff event and the message trace when a conversation goes to the team because of it.</summary>
public static class AiBotBlockReason
{
    /// <summary>The plan doesn't include the AI assistant.</summary>
    public const string PlanWithoutAi = "plan_without_ai_assistant";

    /// <summary>No connected account on the conversation's channel.</summary>
    public const string NoAccount = "account_not_connected";

    /// <summary>AI is off on the account (or it has no settings yet).</summary>
    public const string AiDisabled = "ai_disabled";

    /// <summary>AI is on but no bot is assigned (or the assigned bot was deleted).</summary>
    public const string NoBot = "no_bot";

    /// <summary>The assigned bot is a draft or inactive.</summary>
    public const string BotInactive = "bot_inactive";

    /// <summary>The assigned bot is active but beyond the plan's max_ai_bots (after a downgrade).</summary>
    public const string BotLimit = "ai_bot_limit";

    /// <summary>The bot answers the account, but not conversations a contact reopens.</summary>
    public const string ReopenedToHuman = "reopened_to_human";
}
