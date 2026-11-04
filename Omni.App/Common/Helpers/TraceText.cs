namespace Omni.App.Common.Helpers;

/// <summary>Short, safe text for trace step details (never message content).</summary>
public static class TraceText
{
    /// <summary>"TimeoutException: The operation timed out" capped at <paramref name="max"/> characters.</summary>
    /// <summary>"Type: message", plus the innermost exception when it differs (EF's DbUpdateException only says "see the inner
    /// exception"; the database's own reason is what an admin needs).</summary>
    public static string Error(Exception exception, int max = 300)
    {
        var inner = exception.GetBaseException();
        var text = $"{exception.GetType().Name}: {exception.Message}";
        if (!ReferenceEquals(inner, exception)) text += $" | {inner.GetType().Name}: {inner.Message}";
        return Cap(text, max)!;
    }

    public static string? Cap(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}