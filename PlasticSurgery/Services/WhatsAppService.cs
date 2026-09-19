using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public WhatsAppService(ApplicationDbContext db, HttpClient http, IConfiguration configuration)
    {
        _db = db;
        _http = http;
        _configuration = configuration;
    }

    private string Version => _configuration["Meta:GraphApiVersion"] ?? "v21.0";

    public async Task<string> SendTextMessageAsync(Guid clinicId, string toPhone, string text, CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "text",
            text = new { body = text }
        };
        return await SendAsync(clinicId, payload, ct);
    }

    public async Task<string> SendTemplateMessageAsync(
        Guid clinicId, string toPhone, string templateName, string languageCode,
        IReadOnlyList<string> bodyParameters, CancellationToken ct = default)
    {
        object template = bodyParameters.Count == 0
            ? new { name = templateName, language = new { code = languageCode } }
            : new
            {
                name = templateName,
                language = new { code = languageCode },
                components = new object[]
                {
                    new
                    {
                        type = "body",
                        parameters = bodyParameters.Select(p => new { type = "text", text = p }).ToArray()
                    }
                }
            };

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template
        };
        return await SendAsync(clinicId, payload, ct);
    }

    private async Task<string> SendAsync(Guid clinicId, object payload, CancellationToken ct)
    {
        var integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.WhatsApp, ct);

        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected
            || string.IsNullOrEmpty(integration.PhoneNumberId) || string.IsNullOrEmpty(integration.AccessToken))
        {
            throw new WhatsAppSendException(
                "This clinic doesn't have a connected WhatsApp number yet — see Settings → Channels & Integrations.");
        }

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

public class WhatsAppSendException : ChannelSendException
{
    public WhatsAppSendException(string message) : base(message) { }
}
