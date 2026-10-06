using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>A non-success answer from Infobip. Message is for server logs only (it names Infobip and carries
/// Infobip's own error text, never the API key or the request body) — callers must not show it to users; see
/// InfobipWhatsAppProvider, which turns it into a provider-neutral WhatsAppSendException.</summary>
public class InfobipApiException : Exception
{
    public int? StatusCode { get; }
    public string? ErrorId { get; }
    /// <summary>The call timed out after the request went out — the message may or may not have been sent.</summary>
    public bool IsTimeout { get; init; }
    /// <summary>Infobip's own error text (and validation errors), without our wording around it.</summary>
    public string? Detail { get; init; }
    /// <summary>Infobip refused the message itself (4xx or a synchronous REJECTED), as opposed to being unavailable.</summary>
    public bool IsRejected => StatusCode is >= 400 and < 500 and not 429 || ErrorId?.StartsWith("REJECTED", StringComparison.OrdinalIgnoreCase) == true;

    public InfobipApiException(string message, int? statusCode = null, string? errorId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorId = errorId;
    }
}
