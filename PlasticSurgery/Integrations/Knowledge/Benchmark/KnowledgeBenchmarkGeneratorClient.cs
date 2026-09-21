using System.Text.Json;

namespace PlasticSurgery.Integrations.Knowledge.Benchmark;

/// <summary>A source chunk sent to the question generator. Only the ids and the text — never anything from another clinic.</summary>
public record BenchmarkSourceChunk(Guid DocumentId, Guid ChunkId, string DocumentTitle, string Content);

/// <summary>One question exactly as the generator returned it. Deliberately UNTRUSTED raw strings: the benchmark
/// service validates every id against the current clinic before anything is stored.</summary>
public record RawGeneratedQuestion(string? DocumentId, string? ChunkId, string? Question);

/// <summary>What the generator returned: the generationId it echoed (null if it didn't) and the questions.</summary>
public record GeneratorResponse(string? GenerationId, IReadOnlyList<RawGeneratedQuestion> Questions);

/// <summary>The generator webhook is missing from configuration.</summary>
public class BenchmarkGeneratorNotConfiguredException : Exception
{
    public BenchmarkGeneratorNotConfiguredException(string message) : base(message) { }
}

/// <summary>The generator webhook failed or returned something unusable.</summary>
public class BenchmarkGenerationException : Exception
{
    public BenchmarkGenerationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The one and only link to the benchmark question-generation n8n workflow (a separate workflow from the
/// patient AI agent). Its sole job: send chunks out, parse questions back. It never searches, embeds or scores.
/// </summary>
public interface IKnowledgeBenchmarkGeneratorClient
{
    bool IsConfigured { get; }

    /// <exception cref="BenchmarkGeneratorNotConfiguredException">N8n:KnowledgeBenchmarkWebhookUrl is empty.</exception>
    /// <exception cref="BenchmarkGenerationException">The webhook is unreachable, non-2xx, or returned an unreadable body.</exception>
    Task<GeneratorResponse> GenerateAsync(Guid clinicId, Guid generationId, IReadOnlyList<BenchmarkSourceChunk> chunks, CancellationToken ct = default);
}

/// <summary>
/// POSTs to N8n:KnowledgeBenchmarkWebhookUrl (env var N8n__KnowledgeBenchmarkWebhookUrl):
/// <code>
/// { "generationId": "...", "clinicId": "...", "chunks": [ { "documentId": "...", "chunkId": "...", "documentTitle": "...", "content": "..." } ] }
/// </code>
/// and expects the SAME generationId back:
/// <code>
/// { "generationId": "...", "questions": [ { "documentId": "...", "chunkId": "...", "question": "..." } ] }
/// </code>
/// n8n's "Respond to Webhook" node often wraps the body in a one-element array, so <c>[ { "generationId", "questions" } ]</c>
/// is accepted too. The service checks the echoed id equals the one it sent (see KnowledgeBenchmarkService). The workflow must
/// respond only AFTER the questions are generated (Webhook node: "Respond → Using 'Respond to Webhook' Node") — the call waits.
/// </summary>
public class N8nKnowledgeBenchmarkGeneratorClient : IKnowledgeBenchmarkGeneratorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<N8nKnowledgeBenchmarkGeneratorClient> _logger;

    public N8nKnowledgeBenchmarkGeneratorClient(HttpClient http, IConfiguration configuration, ILogger<N8nKnowledgeBenchmarkGeneratorClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    private string? Url => _configuration["N8n:KnowledgeBenchmarkWebhookUrl"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);

    public async Task<GeneratorResponse> GenerateAsync(Guid clinicId, Guid generationId, IReadOnlyList<BenchmarkSourceChunk> chunks, CancellationToken ct = default)
    {
        var url = Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new BenchmarkGeneratorNotConfiguredException(
                "The benchmark question generator isn't configured — set N8n:KnowledgeBenchmarkWebhookUrl (env var N8n__KnowledgeBenchmarkWebhookUrl).");
        }

        var payload = new
        {
            generationId,
            clinicId,
            chunks = chunks.Select(c => new { documentId = c.DocumentId, chunkId = c.ChunkId, documentTitle = c.DocumentTitle, content = c.Content })
        };

        string body;
        try
        {
            using var response = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);
            body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Benchmark generator webhook returned {StatusCode}.", (int)response.StatusCode);
                throw new BenchmarkGenerationException($"The question generator returned HTTP {(int)response.StatusCode}. Check the n8n workflow.");
            }
        }
        catch (BenchmarkGenerationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Benchmark generator webhook could not be reached.");
            throw new BenchmarkGenerationException("The question generator couldn't be reached or timed out. Check the n8n workflow is active.", ex);
        }

        return Parse(body);
    }

    /// <summary>Tolerant parser for the response shapes described on the class: reads the echoed generationId and the questions.
    /// Anything that isn't a JSON object per question is skipped; the service rejects items with missing/invalid ids and
    /// checks the generationId.</summary>
    internal static GeneratorResponse Parse(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new BenchmarkGenerationException("The question generator returned something that isn't JSON.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement? list = null;
            string? generationId = null;

            if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "questions", out var q) && q.ValueKind == JsonValueKind.Array)
            {
                list = q;
                generationId = ReadString(root, "generationId");
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                // [ { "generationId": "...", "questions": [...] } ]  (Respond to Webhook wrapping) — otherwise a bare array
                // of questions, which has nowhere to carry the generationId (the service will then reject it as missing).
                if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Object
                    && TryGetProperty(root[0], "questions", out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    list = inner;
                    generationId = ReadString(root[0], "generationId");
                }
                else
                {
                    list = root;
                }
            }

            if (list is null)
            {
                throw new BenchmarkGenerationException("The question generator's response has no \"questions\" list.");
            }

            var result = new List<RawGeneratedQuestion>();
            foreach (var item in list.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                result.Add(new RawGeneratedQuestion(
                    ReadString(item, "documentId"), ReadString(item, "chunkId"), ReadString(item, "question")));
            }
            return new GeneratorResponse(generationId, result);
        }
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
