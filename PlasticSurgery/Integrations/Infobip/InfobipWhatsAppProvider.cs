using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Infobip;

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

/// <summary>Infobip wants international numbers as digits only ("+44 7860 099299" -> "447860099299").</summary>
public static class InfobipNumbers
{
    public static string Normalize(string? number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
}

/// <summary>Builds a connection's Infobip webhook URL: one URL per sender (= per channel_integrations row) that
/// takes inbound messages, delivery reports and seen reports alike. The token query value is the row's own
/// random secret (WebhookVerifyToken), checked by InfobipWhatsAppWebhookController. The path is deliberately
/// provider-neutral — it's shown on the settings page, and clinics must not see which provider we use.</summary>
public static class InfobipWebhookUrls
{
    public const string RoutePrefix = "api/integrations/whatsapp/connections";
    public const string TokenQueryName = "token";

    public static string Build(string publicBaseUrl, Guid connectionId, string secret) =>
        $"{publicBaseUrl.TrimEnd('/')}/{RoutePrefix}/{connectionId}/events?{TokenQueryName}={Uri.EscapeDataString(secret)}";

    /// <summary>The URL for an existing connection using App:PublicBaseUrl, or null when that isn't set (sends
    /// then omit notifyUrl and Infobip falls back to whatever is configured in its portal).</summary>
    public static string? ForConnection(IConfiguration configuration, ChannelIntegration integration)
    {
        var baseUrl = configuration["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(integration.WebhookVerifyToken)) return null;
        return Build(baseUrl, integration.Id, integration.WebhookVerifyToken);
    }
}
