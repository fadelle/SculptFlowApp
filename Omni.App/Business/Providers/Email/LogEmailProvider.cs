using Omni.App.Business.Contracts.Providers.Email;
using Omni.App.Entities.Dtos.Email;

namespace Omni.App.Business.Providers.Email;

/// <summary>Development stand-in when Brevo:ApiKey is empty (never used in Production): writes the email's recipient,
/// subject and plain text, links included, to the log at Information level, so a reset or invite link can be opened
/// from the console. Answers a made-up message id.</summary>
public class LogEmailProvider : IEmailProvider
{
    private readonly ILogger<LogEmailProvider> _logger;

    public LogEmailProvider(ILogger<LogEmailProvider> logger)
    {
        _logger = logger;
    }

    public Task<string> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var id = "log-" + Guid.NewGuid().ToString("N");
        _logger.LogInformation("Email {Tag} not sent (no Brevo:ApiKey); to {To}, subject {Subject}:\n{Text}",
            message.Tag, message.ToEmail, message.Subject, message.Text);
        return Task.FromResult(id);
    }
}
