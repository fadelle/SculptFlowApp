namespace Omni.App.Common.Exceptions;

/// <summary>A WhatsApp send failed. HttpStatus, ErrorCode and Meta's readable explanation come from Meta's error JSON when there was
/// one, so a campaign can tell a problem with one recipient from one that fails everyone (QA t8-11: an invalid token failed the whole
/// audience one by one, with Meta's raw JSON as each recipient's reason).</summary>
public class WhatsAppSendException : ChannelSendException
{
    /// <summary>The client has no ready WhatsApp number.</summary>
    public const string NotConnectedKey = "channels.whatsApp.errors.notConnected";

    public WhatsAppSendException(string message) : base(message) { }

    public WhatsAppSendException(string message, int? httpStatus, int? errorCode, string? userMessage) : base(message)
    {
        HttpStatus = httpStatus;
        ErrorCode = errorCode;
        UserMessage = userMessage;
    }

    /// <summary>The HTTP status Meta answered with, when there was a response.</summary>
    public int? HttpStatus { get; }

    /// <summary>Meta's error.code (e.g. 190 = the access token is invalid or expired, 131026 = the message couldn't be delivered).</summary>
    public int? ErrorCode { get; }

    /// <summary>Meta's own readable explanation, never the raw JSON.</summary>
    public string? UserMessage { get; }

    /// <summary>Meta's codes for a problem that isn't about the recipient and fails every message until someone fixes it: the access
    /// token (0, 190, 102), permissions (3, 10, 200-299), the account or number (368 blocked, 131031 locked, 131042 payment, 131045
    /// certificate, 133010 not registered), the template (132001 doesn't exist, 132015 paused, 132016 disabled) or Meta itself
    /// (1, 2, 131016). Without a code, HTTP 401 / 403 / 5xx count too, and so does no WhatsApp connected.</summary>
    public bool StopsEveryone => Message == NotConnectedKey || (ErrorCode is { } code
        ? code is 0 or 1 or 2 or 3 or 10 or 102 or 190 or (>= 200 and <= 299) or 368 or 131016 or 131031 or 131042 or 131045
            or 133010 or 132001 or 132015 or 132016
        : HttpStatus is 401 or 403 or >= 500);

    /// <summary>Meta's rate limits (4, 80007 the app or account; 130429 the number's throughput; 131048 spam rate limit): sending
    /// later works.</summary>
    public bool IsRateLimited => ErrorCode is 4 or 80007 or 130429 or 131048;

    /// <summary>A failed send response as this exception: the readable message (Meta's explanation when its error JSON has one, the
    /// raw body otherwise) with the parts of the error.</summary>
    public static WhatsAppSendException FromResponse(int httpStatus, string reason, string body)
    {
        var meta = MetaGraphApiException.FromResponse(httpStatus, reason, body);
        var detail = meta.UserMessage is null ? body : meta.ErrorCode is { } code ? $"{meta.UserMessage} (code {code})" : meta.UserMessage;
        return new WhatsAppSendException($"WhatsApp send failed ({httpStatus} {reason}): {detail}", httpStatus, meta.ErrorCode, meta.UserMessage); // text-ok: Meta's own words
    }
}
