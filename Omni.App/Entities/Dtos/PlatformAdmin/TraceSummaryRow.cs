using Omni.App.Common.Statics;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One trace in a list: when it ran, how many steps, its first step and, if any, the first thing that failed; and what
/// it came to (Outcome: a TraceResults value, with OutcomeReason for skipped and failed; QA t7-10).</summary>
public record TraceSummaryRow(
    string TraceId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Steps,
    int FailedSteps,
    int Errors,
    Guid? ClientId,
    Guid? ConversationId,
    string? Channel,
    string FirstStep,
    string? FailedStep,
    string? FailedDetail,
    string Outcome = TraceResults.Other,
    string? OutcomeReason = null);