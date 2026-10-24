namespace Omni.App.Entities.Responses.Reports;

/// <summary>How fast customers were answered in the period (waits that started in it). Times are null when nothing was
/// answered.</summary>
public sealed record ResponseTimeStats(int Answered, int Unanswered, TimeSpan? Median, TimeSpan? Average, int WithinFiveMinutes,
    TimeSpan? AiMedian, TimeSpan? TeamMedian);
