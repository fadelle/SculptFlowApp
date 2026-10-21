using System.Net.Http.Json;
using System.Text.Json;
using Omni.App.Business.Contracts.HttpClients.Brevo;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Email;

namespace Omni.App.Business.HttpClients.Brevo;

public class BrevoApiClient : IBrevoApiClient
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _apiBaseUrl;

    public BrevoApiClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _apiKey = configuration["Brevo:ApiKey"]?.Trim();
        // Overridable only so tests can point at a fake API; production always uses Brevo's.
        _apiBaseUrl = (configuration["Brevo:ApiBaseUrl"] ?? "https://api.brevo.com/v3").TrimEnd('/');
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<string> SendAsync(EmailMessage message, string fromEmail, string fromName, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new EmailSendException("Brevo:ApiKey is not set.");

        var payload = new
        {
            sender = new { email = fromEmail, name = fromName },
            to = new[] { string.IsNullOrWhiteSpace(message.ToName) ? (object)new { email = message.ToEmail } : new { email = message.ToEmail, name = message.ToName } },
            subject = message.Subject,
            htmlContent = message.Html,
            textContent = message.Text,
            tags = new[] { message.Tag }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/smtp/email") { Content = JsonContent.Create(payload) };
            request.Headers.Add("api-key", _apiKey);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new EmailSendException($"Brevo refused the email (HTTP {(int)response.StatusCode}): {ErrorOf(body)}", (int)response.StatusCode);
            }
            return MessageIdOf(body) ?? string.Empty;
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new EmailSendException($"Could not reach Brevo ({ex.GetType().Name}).");
        }
    }

    /// <summary>Brevo answers errors as { "code": "...", "message": "..." }; anything else is cut short.</summary>
    private static string ErrorOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
                var text = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (code is not null || text is not null) return $"{code}: {text}";
            }
        }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] : body;
    }

    private static string? MessageIdOf(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("messageId", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
