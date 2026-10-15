using System.Security.Cryptography;
using System.Text;

namespace PlasticSurgery.Common.Helpers;

/// <summary>
/// The fingerprint that groups one problem in activity.app_logs: the same logger, message template, exception type and
/// top stack frame give the same 16 hex characters whatever the logged values were.
/// </summary>
public static class AppLogFingerprint
{
    public static string Compute(string category, string? template, string? exceptionType, string? topFrame)
    {
        var text = string.Join('\n', category, template ?? string.Empty, exceptionType ?? string.Empty, topFrame ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// The first "at ..." line of an exception's stack trace, without the " in file:line" part (line numbers move between
    /// builds). Null when the exception was never thrown.
    /// </summary>
    public static string? TopFrame(Exception? exception)
    {
        var stack = exception?.StackTrace;
        if (string.IsNullOrWhiteSpace(stack)) return null;
        var end = stack.IndexOf('\n');
        var line = (end < 0 ? stack : stack[..end]).Trim();
        var file = line.IndexOf(" in ", StringComparison.Ordinal);
        return file < 0 ? line : line[..file];
    }
}
