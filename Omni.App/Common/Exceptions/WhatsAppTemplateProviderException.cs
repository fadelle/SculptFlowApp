namespace Omni.App.Common.Exceptions;

/// <summary>A template call to Meta failed. Message is Meta's readable explanation (never its raw JSON). CouldNotReach says the
/// template itself wasn't judged: Meta couldn't be reached, or the WhatsApp connection (token, permission) needs renewing, or
/// Meta was busy (QA t8-12: such a failure marked the template "Rejected"). Otherwise Meta refused the template's content.</summary>
public class WhatsAppTemplateProviderException : Exception
{
    public WhatsAppTemplateProviderException(string message, bool couldNotReach = false, Exception? innerException = null)
        : base(message, innerException)
    {
        CouldNotReach = couldNotReach;
    }

    public bool CouldNotReach { get; }

    /// <summary>Meta's error codes that are about the connection or Meta itself, not the template: 190 / 102 (access token invalid
    /// or expired), 10 / 200-299 (permission), 4 / 17 / 32 / 613 / 80007 (rate limits), 1 / 2 (temporary Meta problems).</summary>
    public static bool IsConnectionProblem(MetaGraphApiException ex) =>
        ex.HttpStatus is 401 or 403 or >= 500
        || ex.ErrorCode is 190 or 102 or 10 or (>= 200 and <= 299) or 4 or 17 or 32 or 613 or 80007 or 1 or 2;

    /// <summary>A failed Graph call as this exception: Meta's readable message, and whether it is a connection problem.</summary>
    public static WhatsAppTemplateProviderException From(MetaGraphApiException ex) =>
        new(ex.UserMessage ?? ex.Message, IsConnectionProblem(ex), ex);
}
