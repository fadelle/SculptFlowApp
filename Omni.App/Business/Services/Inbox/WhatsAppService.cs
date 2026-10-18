using Omni.App.Business.Contracts.Providers.WhatsApp;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Business.Services.Inbox;

/// <summary>Front door for WhatsApp sends: finds the client's connected WhatsApp number, checks it's ready, then
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

    public async Task<string> SendTextMessageAsync(Guid clientId, string toPhone, string text, CancellationToken ct = default)
    {
        var integration = await ResolveAsync(clientId, ct);
        return await _provider.SendTextAsync(integration, toPhone, text, ct);
    }

    public async Task<string> SendTemplateMessageAsync(
        Guid clientId, string toPhone, string templateName, string languageCode,
        IReadOnlyList<string> bodyParameters, CancellationToken ct = default)
    {
        var integration = await ResolveAsync(clientId, ct);
        return await _provider.SendTemplateAsync(integration, toPhone,
            new WhatsAppTemplateSend(templateName, languageCode, bodyParameters), ct);
    }

    private async Task<ChannelIntegration> ResolveAsync(Guid clientId, CancellationToken ct)
    {
        var integration = await _integrations.GetReadOnlyAsync(clientId, ChannelType.WhatsApp, ct);
        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected || !_provider.IsReady(integration))
        {
            throw new WhatsAppSendException("channels.whatsApp.errors.notConnected");
        }
        return integration;
    }
}
