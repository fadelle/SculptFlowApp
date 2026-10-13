using PlasticSurgery.Business.Contracts.Providers.WhatsApp;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Services.Inbox;

/// <summary>Front door for WhatsApp sends: finds the clinic's connected WhatsApp number, checks it's ready, then
/// hands the actual API call to IWhatsAppProvider (Meta Cloud API). No HTTP details live here.</summary>
public class WhatsAppService : IWhatsAppService
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IWhatsAppProvider _provider;

    public WhatsAppService(IChannelIntegrationRepository integrations, IWhatsAppProvider provider)
    {
        _integrations = integrations;
        _provider = provider;
    }

    public async Task<string> SendTextMessageAsync(Guid clinicId, string toPhone, string text, CancellationToken ct = default)
    {
        var integration = await ResolveAsync(clinicId, ct);
        return await _provider.SendTextAsync(integration, toPhone, text, ct);
    }

    public async Task<string> SendTemplateMessageAsync(
        Guid clinicId, string toPhone, string templateName, string languageCode,
        IReadOnlyList<string> bodyParameters, CancellationToken ct = default)
    {
        var integration = await ResolveAsync(clinicId, ct);
        return await _provider.SendTemplateAsync(integration, toPhone,
            new WhatsAppTemplateSend(templateName, languageCode, bodyParameters), ct);
    }

    private async Task<ChannelIntegration> ResolveAsync(Guid clinicId, CancellationToken ct)
    {
        var integration = await _integrations.GetReadOnlyAsync(clinicId, ChannelType.WhatsApp, ct);
        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected || !_provider.IsReady(integration))
        {
            throw new WhatsAppSendException(
                "This clinic doesn't have a connected WhatsApp number yet — see Settings → Messaging Integrations.");
        }
        return integration;
    }
}
