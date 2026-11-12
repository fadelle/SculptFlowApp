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
    private readonly IServiceScopeFactory? _scopes;
    private readonly ILogger<WhatsAppService>? _logger;

    public WhatsAppService(IChannelIntegrationRepository integrations, IWhatsAppProvider provider, IServiceScopeFactory? scopes = null,
        ILogger<WhatsAppService>? logger = null)
    {
        _integrations = integrations;
        _provider = provider;
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<string> SendTextMessageAsync(Guid clientId, string toPhone, string text, CancellationToken ct = default)
    {
        var integration = await ResolveAsync(clientId, ct);
        try { return await _provider.SendTextAsync(integration, toPhone, text, ct); }
        catch (WhatsAppSendException ex) when (ex.ConnectionProblemCode is not null)
        {
            await RecordProblemAsync(integration, ex, ct);
            throw;
        }
    }

    /// <summary>The number's health learns from a send that found the connection broken (QA t8-10). In its own scope, so the
    /// failing send's pending changes are neither saved nor lost with it; never hides the send's own error.</summary>
    private async Task RecordProblemAsync(ChannelIntegration integration, WhatsAppSendException failure, CancellationToken ct)
    {
        if (_scopes is null) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<Contracts.Services.WhatsApp.IWhatsAppHealthService>()
                .RecordSendFailureAsync(integration, failure, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "WhatsApp health for client {ClientId} wasn't updated after a failed send.", integration.ClientId);
        }
    }

    public async Task<string> SendTemplateMessageAsync(
        Guid clientId, string toPhone, string templateName, string languageCode,
        IReadOnlyList<string> bodyParameters, CancellationToken ct = default)
    {
        var integration = await ResolveAsync(clientId, ct);
        try
        {
            return await _provider.SendTemplateAsync(integration, toPhone,
                new WhatsAppTemplateSend(templateName, languageCode, bodyParameters), ct);
        }
        catch (WhatsAppSendException ex) when (ex.ConnectionProblemCode is not null)
        {
            await RecordProblemAsync(integration, ex, ct);
            throw;
        }
    }

    public async Task<bool> IsReadyAsync(Guid clientId, CancellationToken ct = default) =>
        IsReady(await _integrations.GetReadOnlyAsync(clientId, ChannelType.WhatsApp, ct));

    private async Task<ChannelIntegration> ResolveAsync(Guid clientId, CancellationToken ct)
    {
        var integration = await _integrations.GetReadOnlyAsync(clientId, ChannelType.WhatsApp, ct);
        if (integration is null || !IsReady(integration))
        {
            throw new WhatsAppSendException(WhatsAppSendException.NotConnectedKey);
        }
        return integration;
    }

    private bool IsReady(ChannelIntegration? integration) =>
        integration is not null && integration.Status == ChannelIntegrationStatus.Connected && _provider.IsReady(integration);
}
