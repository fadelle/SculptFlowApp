namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>What a trace's steps say, for TraceResultRule: counts of the steps that decide the outcome, whether the bot handed
/// the conversation over (a handoff or failed-run action that worked), the first failed and
/// first "skipped on purpose" steps (with their JSON detail), and when the trace was last active.</summary>
public record TraceFacts(
    int Accepted,
    int ModeChanged,
    bool HandoffAction,
    int Triggered,
    int Saved,
    string? FailedStep,
    string? SkipStep,
    string? SkipDetail,
    DateTimeOffset EndedAt);
