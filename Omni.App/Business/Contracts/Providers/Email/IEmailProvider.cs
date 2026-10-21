using Omni.App.Entities.Dtos.Email;

namespace Omni.App.Business.Contracts.Providers.Email;

/// <summary>Sends one email. BrevoEmailProvider when Brevo:ApiKey is set; without a key, LogEmailProvider outside
/// Production (the email, link included, goes to the log so local runs and tests work) and BrevoEmailProvider in
/// Production (which then refuses, so nothing is silently dropped).</summary>
public interface IEmailProvider
{
    /// <summary>Returns the provider's message id. Throws EmailSendException when the email couldn't be sent.</summary>
    Task<string> SendAsync(EmailMessage message, CancellationToken ct = default);
}
