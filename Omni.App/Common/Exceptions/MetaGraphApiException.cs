using System.Text.Json;

namespace Omni.App.Common.Exceptions;

/// <summary>Thrown when a Meta Graph API call returns a non-success response — the message carries Meta's raw error body.
/// HttpStatus, ErrorCode and UserMessage are read from that body when it is Meta's error JSON (QA t8-12: the template page showed
/// the raw JSON, and a bad token was taken for a content refusal).</summary>
public class MetaGraphApiException : Exception
{
    public MetaGraphApiException(string message) : base(message) { }

    public MetaGraphApiException(string message, int? httpStatus, int? errorCode, string? userMessage) : base(message)
    {
        HttpStatus = httpStatus;
        ErrorCode = errorCode;
        UserMessage = userMessage;
    }

    /// <summary>The HTTP status Meta answered with, when there was a response.</summary>
    public int? HttpStatus { get; }

    /// <summary>Meta's error.code (e.g. 190 = the access token is invalid or expired, 100 = a bad parameter).</summary>
    public int? ErrorCode { get; }

    /// <summary>Meta's own readable explanation (error.error_user_msg, else error.message), never the raw JSON.</summary>
    public string? UserMessage { get; }

    /// <summary>A failed response as an exception: the raw body in the message (for logs), the parts of Meta's error JSON in the
    /// properties.</summary>
    public static MetaGraphApiException FromResponse(int httpStatus, string reason, string body)
    {
        int? code = null;
        string? userMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var c) && c.TryGetInt32(out var parsed)) code = parsed;
                userMessage = Text(error, "error_user_msg") ?? Text(error, "message");
            }
        }
        catch (JsonException)
        {
        }
        return new MetaGraphApiException($"Meta Graph API call failed ({httpStatus} {reason}): {body}", httpStatus, code, userMessage); // text-ok: log/diagnostic text
    }

    private static string? Text(JsonElement error, string name) =>
        error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
