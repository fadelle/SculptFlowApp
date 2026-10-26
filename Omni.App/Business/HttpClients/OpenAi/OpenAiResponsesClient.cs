using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Omni.App.Business.Contracts.HttpClients.OpenAi;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.HttpClients.OpenAi;

/// <summary>
/// IAiModelClient over the OpenAI Responses API (POST {Ai / BaseUrl}/responses), like OpenAiEmbeddingService: plain
/// HttpClient, no SDK. Configuration (settings): Ai / ApiKey (secret; empty = Embeddings / ApiKey), Ai / BaseUrl,
/// Ai / TimeoutSeconds (each call; the HttpClient itself has no timeout). Sends the instructions, the input items, the
/// function tools and the json_schema output format; reads the output text, the function calls, every output item (handed
/// back on the next call of a function-call loop) and the token usage.
/// </summary>
public class OpenAiResponsesClient : IAiModelClient
{
    private readonly HttpClient _http;
    private readonly IConfigManager _config;
    private readonly ILogger<OpenAiResponsesClient>? _logger;

    public OpenAiResponsesClient(HttpClient http, IConfigManager config, ILogger<OpenAiResponsesClient>? logger = null)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<AiModelResponse> CreateResponseAsync(AiModelRequest request, CancellationToken ct = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_config.AiApiKey) ? _config.EmbeddingsApiKey : _config.AiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiModelException(
                "The AI model API key isn't set: set Ai / ApiKey (or Embeddings / ApiKey) on the admin portal's Configuration page.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_config.AiBaseUrl.TrimEnd('/')}/responses")
        {
            Content = new StringContent(Body(request).ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var timeout = TimeSpan.FromSeconds(_config.AiTimeoutSeconds);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        var started = Stopwatch.GetTimestamp();
        int status;
        string body;
        try
        {
            using var response = await _http.SendAsync(message, timer.Token);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(timer.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiModelException($"The AI model didn't answer within {timeout.TotalSeconds:0} seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiModelException($"The AI model API is unreachable: {ex.Message}", ex);
        }
        var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (status is < 200 or >= 300)
        {
            // The body can echo a masked copy of the API key, so it is only logged.
            _logger?.LogWarning("AI model request failed ({Status}): {Body}", status, Truncate(body));
            throw new AiModelException($"The AI model API answered HTTP {status}{ErrorCode(body)}.");
        }

        try
        {
            return Read(body, durationMs);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AiModelException("The AI model API returned an unexpected answer.", ex);
        }
    }

    private static JsonObject Body(AiModelRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["input"] = new JsonArray(request.Input.Select(i => (JsonNode)i.Json.DeepClone()).ToArray())
        };
        if (!string.IsNullOrEmpty(request.Instructions)) body["instructions"] = request.Instructions;
        if (request.Tools is { Count: > 0 } tools)
        {
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["parameters"] = t.Parameters.DeepClone(),
                ["strict"] = t.Strict
            }).ToArray());
        }
        if (request.Output is { } output)
        {
            body["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = output.Name,
                    ["schema"] = output.Schema.DeepClone(),
                    ["strict"] = output.Strict
                }
            };
        }
        if (request.Temperature is { } temperature) body["temperature"] = temperature;
        return body;
    }

    private static AiModelResponse Read(string body, double durationMs)
    {
        var root = JsonNode.Parse(body)?.AsObject() ?? throw new JsonException("Empty answer."); // text-ok: wrapped, never shown
        var text = new StringBuilder();
        var hasText = false;
        var calls = new List<AiFunctionCall>();
        var items = new List<AiInputItem>();
        foreach (var node in root["output"]?.AsArray() ?? [])
        {
            if (node is not JsonObject item) continue;
            items.Add(new AiInputItem((JsonObject)item.DeepClone()));
            switch ((string?)item["type"])
            {
                case "function_call":
                    calls.Add(new AiFunctionCall(
                        (string?)item["call_id"] ?? throw new KeyNotFoundException("call_id"),
                        (string?)item["name"] ?? throw new KeyNotFoundException("name"),
                        (string?)item["arguments"] ?? "{}"));
                    break;
                case "message":
                    foreach (var part in item["content"]?.AsArray() ?? [])
                    {
                        if ((string?)part?["type"] != "output_text") continue;
                        text.Append((string?)part["text"]);
                        hasText = true;
                    }
                    break;
            }
        }

        var usage = root["usage"] is JsonObject u
            ? new AiTokenUsage(
                Int(u["input_tokens"]), Int(u["output_tokens"]), Int(u["total_tokens"]),
                Int(u["input_tokens_details"]?["cached_tokens"]), Int(u["output_tokens_details"]?["reasoning_tokens"]))
            : AiTokenUsage.None;
        return new AiModelResponse((string?)root["id"] ?? "", (string?)root["status"] ?? "", hasText ? text.ToString() : null,
            calls, items, usage, durationMs);
    }

    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    // OpenAI's error code (e.g. " (rate_limit_exceeded)"), never its message.
    private static string ErrorCode(string body)
    {
        try
        {
            var code = (string?)JsonNode.Parse(body)?["error"]?["code"] ?? (string?)JsonNode.Parse(body)?["error"]?["type"];
            return string.IsNullOrWhiteSpace(code) ? "" : $" ({code})";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
