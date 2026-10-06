using System.Text.Json;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Contracts.HttpClients.N8n;

/// <summary>
/// The one and only link OUT to the benchmark question-generation n8n workflow (a separate workflow from the patient AI
/// agent). Its sole job: hand the chunks to n8n. It never searches, embeds or scores, and it does not wait for questions —
/// n8n calls back (see KnowledgeBenchmarkIngestController) when they are ready.
/// </summary>
public interface IKnowledgeBenchmarkGeneratorClient
{
    bool IsConfigured { get; }

    /// <exception cref="BenchmarkGeneratorNotConfiguredException">N8n:KnowledgeBenchmarkWebhookUrl is empty.</exception>
    /// <exception cref="BenchmarkGenerationException">The webhook is unreachable or returned a non-2xx status.</exception>
    Task<GeneratorSendResult> SendAsync(Guid clinicId, Guid generationId, IReadOnlyList<BenchmarkSourceChunk> chunks, CancellationToken ct = default);
}
