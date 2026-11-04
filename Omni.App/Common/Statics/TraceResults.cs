namespace Omni.App.Common.Statics;

/// <summary>What a message trace came to, computed from its steps (QA t7-10: a dropped reply or a skipped photo looked
/// successful). See TraceResultRule.</summary>
public static class TraceResults
{
    /// <summary>A message went out (an AI or member reply accepted by the provider).</summary>
    public const string Replied = "replied";
    /// <summary>The conversation moved to the team.</summary>
    public const string HandedOff = "handed_off";
    /// <summary>Something failed and nothing was sent.</summary>
    public const string Failed = "failed";
    /// <summary>Not answered on purpose (human mode, media the AI doesn't read, a stop word, a closed window...); see the reason.</summary>
    public const string Skipped = "skipped";
    /// <summary>The bot was asked, then nothing came of it.</summary>
    public const string NoReply = "no_reply";
    /// <summary>The bot was asked within the last minutes; it may still answer.</summary>
    public const string InProgress = "in_progress";
    /// <summary>The message was saved and nothing was asked of the bot (no step says why).</summary>
    public const string Received = "received";
    /// <summary>Not a customer message's story (a status, a template event, a webhook that named no client...).</summary>
    public const string Other = "other";
}
