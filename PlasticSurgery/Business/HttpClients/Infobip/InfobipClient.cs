using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Business.Contracts.HttpClients.Infobip;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.Infobip;

namespace PlasticSurgery.Business.HttpClients.Infobip;

public class InfobipClient : IInfobipClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InfobipClient> _logger;

    public InfobipClient(HttpClient http, IConfiguration configuration, ILogger<InfobipClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    private string? ApiKey => _configuration["Infobip:ApiKey"]?.Trim();

    /// <summary>Infobip:BaseUrl as an https origin — accepts "xxxx.api.infobip.com" with or without the scheme.</summary>
    private string? BaseUrl
    {
        get
        {
            var raw = _configuration["Infobip:BaseUrl"]?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(raw)) return null;
            return raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? raw
                : "https://" + raw;
        }
    }

    private int MaxSendAttempts => Math.Clamp(_configuration.GetValue("Infobip:MaxSendAttempts", 3), 1, 5);

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(BaseUrl);

    public async Task<InfobipSendResult> SendWhatsAppTextAsync(InfobipWhatsAppTextMessage message, CancellationToken ct = default)
    {
        using var doc = await SendWithRetryAsync("/whatsapp/1/message/text", message, message.MessageId, ct);
        return ReadSendResult(doc.RootElement, message.MessageId);
    }

    public async Task<InfobipSendResult> SendWhatsAppTemplateAsync(InfobipWhatsAppTemplateMessage message, CancellationToken ct = default)
    {
        // The template endpoint is bulk-shaped: { messages: [...] } in, { messages: [...], bulkId } out.
        using var doc = await SendWithRetryAsync("/whatsapp/1/message/template", new { messages = new[] { message } }, message.MessageId, ct);
        var root = doc.RootElement;
        var first = root.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
            ? arr[0]
            : root;
        return ReadSendResult(first, message.MessageId);
    }

    public async Task<InfobipSenderInfo?> GetWhatsAppSenderAsync(string sender, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = NewRequest(HttpMethod.Get, $"/whatsapp/1/senders/{Uri.EscapeDataString(sender)}/business-info");
        using var response = await SendOnceAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, body, "checking the WhatsApp sender");
        }

        using var doc = JsonDocument.Parse(body);
        var displayName = doc.RootElement.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
            ? dn.GetString()
            : null;
        return new InfobipSenderInfo(sender, displayName);
    }

    public async Task<InfobipTemplateInfo> CreateWhatsAppTemplateAsync(string sender, object template, CancellationToken ct = default)
    {
        using var doc = await SendWithRetryAsync($"/whatsapp/2/senders/{Uri.EscapeDataString(sender)}/templates", template, "template", ct);
        return ReadTemplate(doc.RootElement);
    }

    public async Task<InfobipTemplateInfo> GetWhatsAppTemplateAsync(string sender, string templateId, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = NewRequest(HttpMethod.Get,
            $"/whatsapp/2/senders/{Uri.EscapeDataString(sender)}/templates/{Uri.EscapeDataString(templateId)}");
        using var response = await SendOnceAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, body, "reading a WhatsApp template");
        }
        using var doc = JsonDocument.Parse(body);
        return ReadTemplate(doc.RootElement);
    }

    private static InfobipTemplateInfo ReadTemplate(JsonElement element)
    {
        // "id" is documented as a string, but accept a number too.
        var id = element.TryGetProperty("id", out var idEl)
            ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString()
            : null;
        return new InfobipTemplateInfo(
            id ?? throw new InfobipApiException("Infobip's template response had no id."),
            GetString(element, "status") ?? "PENDING");
    }

    private async Task<JsonDocument> SendWithRetryAsync(string path, object payload, string messageId, CancellationToken ct)
    {
        EnsureConfigured();
        var maxAttempts = MaxSendAttempts;

        for (var attempt = 1; ; attempt++)
        {
            using var request = NewRequest(HttpMethod.Post, path);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            HttpResponseMessage response;
            try
            {
                response = await SendOnceAsync(request, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                // No response at all (DNS, refused, reset before headers) — Infobip never got it, safe to retry.
                _logger.LogWarning("Infobip send {MessageId} attempt {Attempt}/{Max} failed to connect ({Error}); retrying.",
                    messageId, attempt, maxAttempts, ex.HttpRequestError);
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new InfobipApiException("Couldn't reach Infobip — the message was not sent.", inner: ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    return JsonDocument.Parse(body);
                }

                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
                if (retryable && attempt < maxAttempts)
                {
                    _logger.LogWarning("Infobip send {MessageId} attempt {Attempt}/{Max} got {StatusCode}; retrying.",
                        messageId, attempt, maxAttempts, (int)response.StatusCode);
                    await Task.Delay(Backoff(attempt), ct);
                    continue;
                }

                var ex = ToException(response.StatusCode, body, "sending a WhatsApp message");
                _logger.LogWarning("Infobip send {MessageId} failed: {StatusCode} {ErrorId}.", messageId, (int)response.StatusCode, ex.ErrorId);
                throw ex;
            }
        }
    }

    /// <summary>One HTTP call. A timeout (HttpClient.Timeout = Infobip:TimeoutSeconds) is reported as an
    /// InfobipApiException and never retried — the request may already have reached Infobip.</summary>
    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InfobipApiException("Infobip didn't answer in time. The message may or may not have been sent.", inner: ex) { IsTimeout = true };
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("App", ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InfobipApiException("Infobip isn't configured on this server (Infobip:BaseUrl / Infobip:ApiKey).");
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

    private static InfobipSendResult ReadSendResult(JsonElement element, string fallbackMessageId)
    {
        var messageId = GetString(element, "messageId") ?? fallbackMessageId;
        string? group = null, name = null;
        if (element.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            group = GetString(status, "groupName");
            name = GetString(status, "name");
        }

        // A synchronous REJECTED means Infobip refused it outright (bad number, sender not allowed...).
        if (string.Equals(group, "REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InfobipApiException($"Infobip rejected the message ({name ?? "REJECTED"}).", errorId: name);
        }
        return new InfobipSendResult(messageId, group, name);
    }

    /// <summary>Infobip errors look like {"requestError":{"serviceException":{"messageId":"BAD_REQUEST","text":"..."}}}.</summary>
    private static InfobipApiException ToException(HttpStatusCode statusCode, string body, string action)
    {
        string? errorId = null, text = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("requestError", out var re)
                && re.TryGetProperty("serviceException", out var se))
            {
                errorId = GetString(se, "messageId");
                text = GetString(se, "text");
                if (se.TryGetProperty("validationErrors", out var ve) && ve.ValueKind == JsonValueKind.Object)
                {
                    text += " " + ve.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (gateway error page etc.) — fall through to the generic text.
        }

        return new InfobipApiException(
            $"Infobip error while {action} ({(int)statusCode} {statusCode}){(text is null ? "" : ": " + text)}",
            (int)statusCode, errorId) { Detail = text?.Trim() };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
