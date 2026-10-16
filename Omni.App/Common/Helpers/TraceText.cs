namespace Omni.App.Common.Helpers;

/// <summary>Short, safe text for trace step details (never message content).</summary>
public static class TraceText
{
    /// <summary>"TimeoutException: The operation timed out" capped at <paramref name="max"/> characters.</summary>
    public static string Error(Exception exception, int max = 300) => Cap($"{exception.GetType().Name}: {exception.Message}", max)!;

    public static string? Cap(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}