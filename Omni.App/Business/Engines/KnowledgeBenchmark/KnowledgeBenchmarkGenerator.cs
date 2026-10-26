using Omni.App.Business.Contracts.Engines.KnowledgeBenchmark;
using Omni.App.Business.Contracts.HttpClients.OpenAi;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;

namespace Omni.App.Business.Engines.KnowledgeBenchmark;

/// <summary>
/// The retrieval benchmark workflow (docs/n8n/omni-retrieval-benchmark.json) as code: "AI Agent" (model Benchmark /
/// GeneratorModel, system prompt AiPrompts.BenchmarkSystem, input = the payload n8n received, as JSON) → "send_questions"
/// (the agent's output parsed as JSON and delivered like the callback: GeneratorResponseParser, then
/// ReceiveGenerationResultAsync). A model failure or an output that isn't a questions reply fails the generation with the
/// reason, where n8n's run would have stopped and the generation timed out.
/// </summary>
public class KnowledgeBenchmarkGenerator : IKnowledgeBenchmarkGenerator
{
    private readonly IAiModelClient _model;
    private readonly IKnowledgeBenchmarkService _benchmark;
    private readonly IConfigManager _config;
    private readonly ILogger<KnowledgeBenchmarkGenerator> _logger;

    public KnowledgeBenchmarkGenerator(IAiModelClient model, IKnowledgeBenchmarkService benchmark, IConfigManager config,
        ILogger<KnowledgeBenchmarkGenerator> logger)
    {
        _model = model;
        _benchmark = benchmark;
        _config = config;
        _logger = logger;
    }

    public async Task GenerateAsync(BenchmarkGeneratorPayload payload, CancellationToken ct = default)
    {
        string output;
        try
        {
            var response = await _model.CreateResponseAsync(new AiModelRequest(_config.BenchmarkGeneratorModel,
                AiPromptTemplate.Load(AiPrompts.BenchmarkSystem), [AiInputItem.User(JsJson.Stringify(payload))]), ct);
            output = response.OutputText ?? "";
        }
        catch (AiModelException ex)
        {
            _logger.LogWarning(ex, "Benchmark generation {GenerationId}: the AI model failed.", payload.GenerationId);
            await _benchmark.FailGenerationAsync(payload.GenerationId, $"The question generator failed: {ex.Message}", ct); // text-ok: admin portal only
            return;
        }

        GeneratorResponse reply;
        try
        {
            reply = GeneratorResponseParser.Parse(output);
        }
        catch (BenchmarkGenerationException ex)
        {
            await _benchmark.FailGenerationAsync(payload.GenerationId, ex.Message, ct);
            return;
        }

        try
        {
            await _benchmark.ReceiveGenerationResultAsync(payload.GenerationId, reply, output, requireEcho: false, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or BenchmarkGenerationException)
        {
            // The callback would have answered 400 (another generationId in the reply) and n8n's run would have failed.
            await _benchmark.FailGenerationAsync(payload.GenerationId, ex.Message, ct);
        }
    }
}
