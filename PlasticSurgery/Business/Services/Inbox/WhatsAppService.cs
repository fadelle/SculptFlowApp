using PlasticSurgery.Business.Contracts.Providers.WhatsApp;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Services.Inbox;

/// <summary>Provider-independent front door for WhatsApp sends: finds the clinic's WhatsApp connection,
/// checks it belongs to the active provider (the global WhatsApp:Provider switch) and is ready, then hands
/// the actual API call to that IWhatsAppProvider. No provider's HTTP details live here.</summary>
public class WhatsAppService : IWhatsAppService
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IEnumerable<IWhatsAppProvider> _providers;
    private readonly IConfiguration _configuration;

    public WhatsAppService(IChannelIntegrationRepository integrations, IEnumerable<IWhatsAppProvider> providers, IConfiguration configuration)
    {
        _integrations = integrations;
        _providers = providers;
        _configuration = configuration;
    }

    /// <summary>The global switch: "meta" (default) or "infobip".</summary>
    public static string ActiveProviderName(IConfiguration configuration)
    {
        var configured = configuration["WhatsApp:Provider"];
        return string.IsNullOrWhiteSpace(configured) ? ChannelProvider.Meta : configured.Trim().ToLowerInvariant();
    }

    public async Task<string> SendTextMessageAsync(Guid clinicId, string toPhone, string text, CancellationToken ct = default)
    {
        var (provider, integration) = await ResolveAsync(clinicId, ct);
        return await provider.SendTextAsync(integration, toPhone, text, ct);
    }

    public async Task<string> SendTemplateMessageAsync(
        Guid clinicId, string toPhone, string templateName, string languageCode,
        IReadOnlyList<string> bodyParameters, CancellationToken ct = default)
    {
        var (provider, integration) = await ResolveAsync(clinicId, ct);
        return await provider.SendTemplateAsync(integration, toPhone,
            new WhatsAppTemplateSend(templateName, languageCode, bodyParameters), ct);
    }

    private async Task<(IWhatsAppProvider Provider, ChannelIntegration Integration)> ResolveAsync(Guid clinicId, CancellationToken ct)
    {
        var activeName = ActiveProviderName(_configuration);
        var provider = _providers.FirstOrDefault(p => p.Name == activeName)
            ?? throw new WhatsAppSendException($"WhatsApp provider '{activeName}' isn't available — check the WhatsApp:Provider setting.");

        var integration = await _integrations.GetReadOnlyAsync(clinicId, ChannelType.WhatsApp, ct);

        if (integration is not null && integration.Status == ChannelIntegrationStatus.Connected
            && ChannelProvider.Of(integration) != provider.Name)
        {
            // Connected through the other provider (e.g. a Meta Embedded Signup row while Infobip is active).
            throw new WhatsAppSendException(
                "This clinic's WhatsApp number was connected through a different provider — reconnect it in Settings → Messaging Integrations.");
        }

        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected || !provider.IsReady(integration))
        {
            throw new WhatsAppSendException(
                "This clinic doesn't have a connected WhatsApp number yet — see Settings → Messaging Integrations.");
        }

        return (provider, integration);
    }
}
