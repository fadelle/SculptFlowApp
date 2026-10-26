using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;

namespace Omni.App.Business.Engines.KnowledgeBenchmark;

/// <summary>
/// The benchmark question generator KnowledgeBenchmarkService talks to (IKnowledgeBenchmarkGeneratorClient). Benchmark /
/// GeneratorEngine, read for every generation, picks the engine: n8n → N8nKnowledgeBenchmarkGeneratorClient unchanged
/// (posts the chunks; n8n calls back); code → the generation is queued for KnowledgeBenchmarkGenerationWorker and
/// acknowledged at once, like n8n's "workflow started", so the admin call answers "pending" as today.
/// </summary>
public class KnowledgeBenchmarkGeneratorDispatcher : IKnowledgeBenchmarkGeneratorClient
{
    private readonly IConfigManager _config;
    private readonly N8nKnowledgeBenchmarkGeneratorClient _n8n;
    private readonly IKnowledgeBenchmarkGenerationQueue _queue;

    public KnowledgeBenchmarkGeneratorDispatcher(IConfigManager config, N8nKnowledgeBenchmarkGeneratorClient n8n, IKnowledgeBenchmarkGenerationQueue queue)
    {
        _config = config;
        _n8n = n8n;
        _queue = queue;
    }

    private bool UsesCode => string.Equals(_config.BenchmarkGeneratorEngine, WorkflowEngines.Code, StringComparison.OrdinalIgnoreCase);

    /// <summary>n8n: its webhook URL is set; code: an AI model key is set (Ai / ApiKey or Embeddings / ApiKey).</summary>
    public bool IsConfigured => UsesCode
        ? !string.IsNullOrWhiteSpace(_config.AiApiKey) || !string.IsNullOrWhiteSpace(_config.EmbeddingsApiKey)
        : _n8n.IsConfigured;

    public Task<GeneratorSendResult> SendAsync(Guid clientId, Guid generationId, IReadOnlyList<BenchmarkSourceChunk> chunks, CancellationToken ct = default)
    {
        if (!UsesCode) return _n8n.SendAsync(clientId, generationId, chunks, ct);

        if (!_queue.TryEnqueue(new BenchmarkGeneratorPayload(generationId, clientId, chunks)))
        {
            throw new BenchmarkGenerationException("The question generator isn't accepting work right now (the app is restarting). Try again."); // text-ok: admin portal only
        }
        return Task.FromResult(new GeneratorSendResult(null, null));
    }
}
