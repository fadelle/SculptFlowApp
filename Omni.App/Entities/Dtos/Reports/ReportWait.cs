namespace Omni.App.Entities.Dtos.Reports;

/// <summary>A customer waiting for an answer: the first message after the last reply, when (and whether by the AI) it
/// was answered. RepliedAt is null while unanswered.</summary>
public sealed record ReportWait(DateTimeOffset WaitingSince, DateTimeOffset? RepliedAt, bool RepliedByAi);
