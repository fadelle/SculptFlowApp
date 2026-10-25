namespace Omni.App.Common.Enums;

/// <summary>crm.bots.handoff_policy: when the bot hands a conversation to the team. An explicit request for a person is
/// always honoured.</summary>
public static class BotHandoffPolicy
{
    /// <summary>Only when the contact asks for a person.</summary>
    public const string OnRequest = "on_request";

    /// <summary>When the contact asks for a person or the bot can't confidently help (the default).</summary>
    public const string OnRequestOrUnsure = "on_request_or_unsure";

    public static readonly IReadOnlyList<string> All = [OnRequestOrUnsure, OnRequest];
}
