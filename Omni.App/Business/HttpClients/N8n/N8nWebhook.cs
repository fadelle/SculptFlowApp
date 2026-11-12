using System.Net.Http.Json;
using System.Text.Json;

namespace Omni.App.Business.HttpClients.N8n;

/// <summary>
/// Posts a trigger to an n8n webhook. With N8n:WebhookSecret set, every post carries it as <see cref="SecretHeader"/>, so the
/// workflows' webhook nodes can use Header Auth and refuse anyone else who learns the URL (QA t4-ai-09: the webhooks had no
/// auth, so anyone with the URL could start AI runs at the business's OpenAI cost). Without the setting nothing is added.
/// </summary>
public static class N8nWebhook
{
    public const string SecretHeader = "X-Omni-Webhook-Secret";
    public const string SecretKey = "N8n:WebhookSecret";

    public static async Task<HttpResponseMessage> PostAsync<T>(HttpClient http, string url, T payload, JsonSerializerOptions options,
        IConfiguration configuration, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload, options: options) };
        if (configuration[SecretKey] is { } secret && !string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.TryAddWithoutValidation(SecretHeader, secret.Trim());
        }
        return await http.SendAsync(request, ct);
    }
}
