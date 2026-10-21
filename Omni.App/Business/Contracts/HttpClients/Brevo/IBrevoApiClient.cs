using Omni.App.Entities.Dtos.Email;

namespace Omni.App.Business.Contracts.HttpClients.Brevo;

/// <summary>Brevo's transactional email API (POST /v3/smtp/email). The API key is a secret (Brevo:ApiKey in
/// user-secrets / the environment, never a setting).</summary>
public interface IBrevoApiClient
{
    /// <summary>True when Brevo:ApiKey is set.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one email and returns Brevo's message id. Throws EmailSendException when Brevo refuses it or can't
    /// be reached.</summary>
    Task<string> SendAsync(EmailMessage message, string fromEmail, string fromName, CancellationToken ct = default);
}
