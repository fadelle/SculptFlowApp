namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A page of activity.app_logs rows, newest first; <see cref="Total"/> counts every matching row.</summary>
public record AppLogPage(
    DateTimeOffset From,
    DateTimeOffset To,
    int Page,
    int PageSize,
    int Total,
    List<AppLogRow> Items);