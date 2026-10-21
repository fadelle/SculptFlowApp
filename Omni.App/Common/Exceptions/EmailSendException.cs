namespace Omni.App.Common.Exceptions;

/// <summary>The email provider (Brevo) refused or couldn't take an email. [ApiErrors] maps it to 502. The message holds
/// the provider's answer for the log, never the API key; people see a fixed text instead (EmailSendException.TextKey).</summary>
public class EmailSendException : Exception
{
    /// <summary>What a person is shown when an email couldn't be sent.</summary>
    public const string TextKey = "emails.errors.notSent";

    public int StatusCode { get; }

    public EmailSendException(string message, int statusCode = 0) : base(message)
    {
        StatusCode = statusCode;
    }
}
