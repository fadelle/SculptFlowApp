namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A page of traces, latest activity first; <see cref="Total"/> counts every matching trace.</summary>
public record MessageTracePage(
    DateTimeOffset From,
    DateTimeOffset To,
    int Page,
    int PageSize,
    int Total,
    bool Enabled,
    int RetentionDays,
    int FailedRetentionDays,
    List<TraceSummaryRow> Items);