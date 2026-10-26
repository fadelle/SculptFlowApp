using System.Threading.Channels;
using Omni.App.Business.Contracts.Engines.KnowledgeBenchmark;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Writes queued benchmark questions (Benchmark / GeneratorEngine = code) one generation at a time, each in its own DI scope,
/// with KnowledgeBenchmarkGenerator. A generation still queued when the app stops stays pending and times out
/// (Benchmark / GenerationTimeoutMinutes), like an n8n run that never called back.
/// </summary>
public sealed class KnowledgeBenchmarkGenerationWorker : BackgroundService
{
    private readonly IKnowledgeBenchmarkGenerationQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KnowledgeBenchmarkGenerationWorker> _logger;

    public KnowledgeBenchmarkGenerationWorker(IKnowledgeBenchmarkGenerationQueue queue, IServiceScopeFactory scopes,
        ILogger<KnowledgeBenchmarkGenerationWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            BenchmarkGeneratorPayload payload;
            try
            {
                payload = await _queue.DequeueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                break;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IKnowledgeBenchmarkGenerator>().GenerateAsync(payload, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The generator records its own failures; this only guards the loop.
                _logger.LogError(ex, "Benchmark generation {GenerationId} failed.", payload.GenerationId);
            }
        }
    }

    // A worker stopped before ExecuteAsync ever ran must still refuse new work.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        await base.StopAsync(cancellationToken);
    }
}
