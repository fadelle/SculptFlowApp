namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One trace in a list: when it ran, how many steps, its first step and, if any, the first thing that failed.</summary>
public record TraceSummaryRow(
    string TraceId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Steps,
    int FailedSteps,
    int Errors,
    Guid? ClinicId,
    Guid? ConversationId,
    string? Channel,
    string FirstStep,
    string? FailedStep,
    string? FailedDetail);