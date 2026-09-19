using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PlasticSurgery.Services;

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
    private const int MaxInputsPerRequest = 64;

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public OpenAiEmbeddingService(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    private string Model => _configuration["Embeddings:Model"] is { Length: > 0 } m ? m : "text-embedding-3-small";
    private string BaseUrl => (_configuration["Embeddings:BaseUrl"] is { Length: > 0 } u ? u : "https://api.openai.com/v1").TrimEnd('/');

    public int Dimensions => int.TryParse(_configuration["Embeddings:Dimensions"], out var d) && d > 0 ? d : 1536;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        (await EmbedBatchAsync(new[] { text }, ct))[0];

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var apiKey = _configuration["Embeddings:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Embeddings:ApiKey is not configured — set it via user-secrets or the Embeddings__ApiKey environment variable.");
        }

        var results = new List<float[]>(texts.Count);
        for (var offset = 0; offset < texts.Count; offset += MaxInputsPerRequest)
        {
            var batch = texts.Skip(offset).Take(MaxInputsPerRequest).ToList();
            results.AddRange(await EmbedOneRequestAsync(batch, apiKey, ct));
        }
        return results;
    }

    private async Task<IReadOnlyList<float[]>> EmbedOneRequestAsync(List<string> batch, string apiKey, CancellationToken ct)
    {
        var payload = new Dictionary<string, object> { ["model"] = Model, ["input"] = batch };
        if (Model.StartsWith("text-embedding-3", StringComparison.OrdinalIgnoreCase))
        {
            payload["dimensions"] = Dimensions;
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
            throw new InvalidOperationException($"Embedding request failed ({(int)status}): {Truncate(body)}");
        }

        var vectors = new float[batch.Count][];
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var values = item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (values.Length != Dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding model '{Model}' returned {values.Length} dimensions but Embeddings:Dimensions is {Dimensions} " +
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
