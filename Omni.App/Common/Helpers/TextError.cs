using Microsoft.Extensions.Localization;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Server messages in more than one language: a service throws its usual exception with a strings-file key as the
/// message (<c>throw new ArgumentException("businessTypes.errors.notFound").WithArgs(code)</c>) and [ApiErrors] puts it
/// into the request's language at the edge. A message that isn't a key is shown as written.
/// </summary>
public static class TextError
{
    private const string ArgsDataName = "omni.text.args";

    /// <summary>Attaches the values for the key's {0}, {1}… placeholders.</summary>
    public static TException WithArgs<TException>(this TException exception, params object?[] args) where TException : Exception
    {
        exception.Data[ArgsDataName] = args;
        return exception;
    }

    public static object?[] ArgsOf(Exception exception) => exception.Data[ArgsDataName] as object?[] ?? [];

    /// <summary>The exception's message in the request's language when it is a strings-file key, else as written (also
    /// when there is no localizer).</summary>
    public static string Localize(this IStringLocalizer? text, Exception exception)
    {
        if (text is null) return exception.Message;
        var localized = text[exception.Message, ArgsOf(exception)!];
        return localized.ResourceNotFound ? exception.Message : localized.Value;
    }
}
