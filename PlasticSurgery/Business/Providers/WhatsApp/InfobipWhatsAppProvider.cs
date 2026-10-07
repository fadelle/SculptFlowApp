using PlasticSurgery.Business.Contracts.HttpClients.Infobip;
using PlasticSurgery.Business.Contracts.Providers.WhatsApp;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Infobip;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Providers.WhatsApp;

/// <summary>
/// WhatsApp through Infobip (the MVP provider while SculptFlow has no Meta Tech Provider status). Sends from
/// the clinic's own sender number on SculptFlow's single Infobip account.
///
/// Message ids: we choose the id (a new GUID) and pass it as Infobip's messageId, so the id stored as
/// Message.ExternalMessageId is known even before Infobip answers, and every delivery/seen report Infobip sends
/// for it carries the same value — that's how callbacks map back to our Message row (MessageStatusHandler).
/// notifyUrl points this message's delivery + seen reports at the clinic's own webhook URL.
/// </summary>
public class InfobipWhatsAppProvider : IWhatsAppProvider
{
    private readonly IInfobipClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InfobipWhatsAppProvider> _logger;

    public InfobipWhatsAppProvider(IInfobipClient client, IConfiguration configuration, ILogger<InfobipWhatsAppProvider> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => ChannelProvider.Infobip;

    public bool IsReady(ChannelIntegration integration) => !string.IsNullOrEmpty(integration.ProviderSenderId);

    public async Task<string> SendTextAsync(ChannelIntegration integration, string toPhone, string text, CancellationToken ct = default)
    {
        var message = new InfobipWhatsAppTextMessage(
            From: integration.ProviderSenderId!,
            To: InfobipNumbers.Normalize(toPhone),
            MessageId: Guid.NewGuid().ToString(),
            Content: new InfobipTextContent(text),
            NotifyUrl: InfobipWebhookUrls.ForConnection(_configuration, integration));

        return await SendAsync(() => _client.SendWhatsAppTextAsync(message, ct));
    }

    public async Task<string> SendTemplateAsync(ChannelIntegration integration, string toPhone, WhatsAppTemplateSend template, CancellationToken ct = default)
    {
        var message = new InfobipWhatsAppTemplateMessage(
            From: integration.ProviderSenderId!,
            To: InfobipNumbers.Normalize(toPhone),
            MessageId: Guid.NewGuid().ToString(),
            Content: new InfobipTemplateContent(
                template.Name,
                new InfobipTemplateData(new InfobipTemplateBody(template.BodyParameters)),
                template.LanguageCode),
            NotifyUrl: InfobipWebhookUrls.ForConnection(_configuration, integration));

        return await SendAsync(() => _client.SendWhatsAppTemplateAsync(message, ct));
    }

    private async Task<string> SendAsync(Func<Task<InfobipSendResult>> send)
    {
        try
        {
            var result = await send();
            return result.MessageId;
        }
        catch (InfobipApiException ex)
        {
            // Clinics must never see that WhatsApp runs through Infobip: the detailed error stays in the server
            // log, and staff/campaign failure reasons get a neutral sentence. Same exception family as the Meta
            // provider, so controllers keep mapping it to 502.
            _logger.LogWarning(ex, "WhatsApp send through Infobip failed ({StatusCode} {ErrorId}).", ex.StatusCode, ex.ErrorId);
            throw new WhatsAppSendException(
                ex.IsTimeout ? "WhatsApp didn't confirm the message in time. It may or may not have been sent."
                : ex.IsRejected ? "WhatsApp didn't accept this message. Check the patient's number and try again."
                : "WhatsApp couldn't send the message right now. Try again in a minute.");
        }
    }
}
