using System.Text.Json;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Common.Helpers;

/// <summary>Checks on values an integration posts (n8n, the ingest key) before anything is stored, so bad input is a 400 naming
/// the field instead of a database error (QA t8-06: a 40-character message type, "{not json}" metadata).</summary>
public static class RequestChecks
{
    public const string TooLongKey = "common.errors.fieldTooLong";
    public const string NotJsonKey = "common.errors.fieldNotJson";

    /// <summary>ArgumentException (400) when <paramref name="value"/> is longer than its column.</summary>
    public static void MaxLength(string? value, int max, string field)
    {
        if (value is not null && value.Length > max) throw new ArgumentException(TooLongKey).WithArgs(TextArg.Value(field), max);
    }

    /// <summary>ArgumentException (400) when <paramref name="value"/> is set but isn't JSON (it goes into a jsonb column).</summary>
    public static void Json(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            throw new ArgumentException(NotJsonKey).WithArgs(TextArg.Value(field));
        }
    }
}
