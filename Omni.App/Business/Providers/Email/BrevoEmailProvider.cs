using Omni.App.Business.Contracts.HttpClients.Brevo;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Email;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Email;

namespace Omni.App.Business.Providers.Email;

/// <summary>Email through Brevo's transactional API, from the Email:FromAddress / Email:FromName settings (the address must
/// belong to a domain verified in Brevo).</summary>
public class BrevoEmailProvider : IEmailProvider
{
    private readonly IBrevoApiClient _brevo;
    private readonly IConfigManager _config;
    private readonly ILogger<BrevoEmailProvider> _logger;

    public BrevoEmailProvider(IBrevoApiClient brevo, IConfigManager config, ILogger<BrevoEmailProvider> logger)
    {
        _brevo = brevo;
        _config = config;
        _logger = logger;
    }

    public async Task<string> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var from = _config.EmailFromAddress;
        if (from.Length == 0) throw new EmailSendException("The Email:FromAddress setting is empty.");
        var id = await _brevo.SendAsync(message, from, _config.EmailFromName, ct);
        _logger.LogInformation("Email {Tag} sent through Brevo ({MessageId}).", message.Tag, id);
        return id;
    }
}
