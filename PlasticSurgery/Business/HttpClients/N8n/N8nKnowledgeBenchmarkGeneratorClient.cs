using System.Text.Json;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Business.Engines.KnowledgeBenchmark;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Business.HttpClients.N8n;

/// <summary>
/// POSTs to N8n:KnowledgeBenchmarkWebhookUrl (env var N8n__KnowledgeBenchmarkWebhookUrl):
/// <code>
/// { "generationId": "...", "clinicId": "...", "chunks": [ { "documentId": "...", "chunkId": "...", "documentTitle": "...", "content": "..." } ] }
/// </code>
/// The n8n Webhook node should be set to "Respond → Immediately": any 2xx is an acknowledgement. When the questions are ready
/// n8n POSTs them to <c>/api/knowledge/benchmark/generations/{generationId}/questions</c> with the X-Ingest-Key header.
/// (A workflow that still answers synchronously with <c>{ generationId, questions }</c> keeps working: that reply is processed
/// immediately, as if it had been a callback.)
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

    public async Task<GeneratorSendResult> SendAsync(Guid clinicId, Guid generationId, IReadOnlyList<BenchmarkSourceChunk> chunks, CancellationToken ct = default)
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

        // Normal (asynchronous) workflows answer with something like {"message":"Workflow was started"} — no questions list.
        return new GeneratorSendResult(GeneratorResponseParser.TryParse(body, out var immediate) ? immediate : null, body);
    }
}
