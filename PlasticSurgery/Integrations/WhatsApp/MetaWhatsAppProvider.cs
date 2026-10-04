using System.Text;
using System.Text.Json;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp;

/// <summary>Meta Cloud API directly (graph.facebook.com/{v}/{PhoneNumberId}/messages) with the clinic's own
/// access token from Embedded Signup. Moved here unchanged from WhatsAppService when the provider seam was
/// added — see Services/IWhatsAppProvider.cs.</summary>
public class MetaWhatsAppProvider : IWhatsAppProvider
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public MetaWhatsAppProvider(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public string Name => ChannelProvider.Meta;

    private string Version => _configuration["Meta:GraphApiVersion"] ?? "v21.0";

    public bool IsReady(ChannelIntegration integration) =>
        !string.IsNullOrEmpty(integration.PhoneNumberId) && !string.IsNullOrEmpty(integration.AccessToken);

    public Task<string> SendTextAsync(ChannelIntegration integration, string toPhone, string text, CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "text",
            text = new { body = text }
        };
        return SendAsync(integration, payload, ct);
    }

    public Task<string> SendTemplateAsync(ChannelIntegration integration, string toPhone, WhatsAppTemplateSend template, CancellationToken ct = default)
    {
        object content = template.BodyParameters.Count == 0
            ? new { name = template.Name, language = new { code = template.LanguageCode } }
            : new
            {
                name = template.Name,
                language = new { code = template.LanguageCode },
                components = new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = template.BodyParameters.Select(p => new { type = "text", text = p }).ToArray()
                    }
                }
            };

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template = content
        };
        return SendAsync(integration, payload, ct);
    }

    private async Task<string> SendAsync(ChannelIntegration integration, object payload, CancellationToken ct)
    {
        var url = $"https://graph.facebook.com/{Version}/{integration.PhoneNumberId}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", integration.AccessToken);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new WhatsAppSendException($"WhatsApp send failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var messageId = doc.RootElement
            .GetProperty("messages")[0]
            .GetProperty("id")
            .GetString();

        return messageId ?? throw new WhatsAppSendException($"WhatsApp send response had no message id: {body}");
    }
}
