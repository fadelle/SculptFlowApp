using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Common.Helpers;

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
