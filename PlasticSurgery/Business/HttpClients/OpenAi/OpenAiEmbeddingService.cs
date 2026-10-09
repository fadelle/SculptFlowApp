using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PlasticSurgery.Business.Contracts.HttpClients.OpenAi;
using PlasticSurgery.Business.Contracts.Managers;

namespace PlasticSurgery.Business.HttpClients.OpenAi;

/// <summary>
/// IEmbeddingService over any OpenAI-compatible POST {BaseUrl}/embeddings endpoint. Configuration:
///   Embeddings:ApiKey      (secret — env var Embeddings__ApiKey / user-secrets, never committed)
///   Embeddings:Model       (default text-embedding-3-small)
///   Embeddings:Dimensions  (default 1536 — must match knowledge_chunks.embedding's vector(N))
///   Embeddings:BaseUrl     (default https://api.openai.com/v1)
/// For text-embedding-3-* models the configured Dimensions is also sent to the API so the returned
/// vector length is guaranteed; every response is length-checked either way, so a model/dimension
/// mismatch fails loudly here instead of as an obscure pgvector error at insert time.
/// </summary>
public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly IConfigManager _config;

    private readonly ILogger<OpenAiEmbeddingService>? _logger;

    public OpenAiEmbeddingService(HttpClient http, IConfiguration configuration, IConfigManager config, ILogger<OpenAiEmbeddingService>? logger = null)
    {
        _logger = logger;
        _config = config;
        _http = http;
        _configuration = configuration;
    }

    private string Model => _config.EmbeddingsModel;
    private string BaseUrl => _config.EmbeddingsBaseUrl.TrimEnd('/');

    public int Dimensions => _config.EmbeddingsDimensions;

    public async Task<float[]> EmbedAsync(string text, string? model = null, int? dimensions = null, CancellationToken ct = default) =>
        (await EmbedBatchAsync(new[] { text }, model, dimensions, ct))[0];

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, string? model = null, int? dimensions = null, CancellationToken ct = default)
    {
        var apiKey = _config.EmbeddingsApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "The embeddings API key isn't set: set Embeddings / ApiKey on the admin portal's Configuration page.");
        }

        var results = new List<float[]>(texts.Count);
        var batchSize = _config.EmbeddingsMaxInputsPerRequest;
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToList();
            results.AddRange(await EmbedOneRequestAsync(batch, apiKey, model ?? Model, dimensions ?? Dimensions, ct));
        }
        return results;
    }

    private async Task<IReadOnlyList<float[]>> EmbedOneRequestAsync(List<string> batch, string apiKey, string model, int dimensions, CancellationToken ct)
    {
        var payload = new Dictionary<string, object> { ["model"] = model, ["input"] = batch };
        if (model.StartsWith("text-embedding-3", StringComparison.OrdinalIgnoreCase))
        {
            payload["dimensions"] = dimensions;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Every provider failure surfaces as InvalidOperationException so callers (dashboard pages/API and
        // POST /api/ai/knowledge/search) can turn it into a clean "embedding unavailable" response
        // instead of an unhandled 500.
        string body;
        HttpStatusCode status;
        try
        {
            using var response = await _http.SendAsync(request, ct);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Embedding provider unreachable: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Embedding provider timed out.", ex);
        }

        if ((int)status is < 200 or >= 300)
        {
            // The body can echo a masked copy of the API key and this message reaches clinic users, so only log it.
            _logger?.LogWarning("Embedding request failed ({Status}): {Body}", (int)status, Truncate(body));
            throw new InvalidOperationException($"The embedding service is unavailable (HTTP {(int)status}).");
        }

        var vectors = new float[batch.Count][];
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var values = item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (values.Length != dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding model '{model}' returned {values.Length} dimensions but the configured dimension is {dimensions} " +
                        "(the knowledge_chunks.embedding column must be vector(N) with the same N).");
                }
                vectors[index] = values;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException { InnerException: not null } or IndexOutOfRangeException)
        {
            throw new InvalidOperationException("Embedding provider returned an unexpected response.", ex);
        }

        if (vectors.Any(v => v is null))
        {
            throw new InvalidOperationException("Embedding response did not contain a vector for every input.");
        }
        return vectors;
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
