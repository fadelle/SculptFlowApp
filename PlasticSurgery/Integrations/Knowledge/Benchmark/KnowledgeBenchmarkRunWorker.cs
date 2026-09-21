using System.Threading.Channels;

namespace PlasticSurgery.Integrations.Knowledge.Benchmark;

/// <summary>Hands a queued benchmark run (a knowledge_retrieval_benchmark_runs id) to the background worker.</summary>
public interface IKnowledgeBenchmarkRunQueue
{
    void Enqueue(Guid runId);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}

public sealed class KnowledgeBenchmarkRunQueue : IKnowledgeBenchmarkRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid runId) => _channel.Writer.TryWrite(runId);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}

/// <summary>
/// Executes benchmark runs in the background, one at a time, each in its own DI scope. The request that starts a run only
/// writes a `pending` row and enqueues its id; progress lives in the database, so any page load can show it. A restart is
/// recoverable: runs a previous process left `running` are marked failed, and `pending` ones are queued again.
/// Failure isolation: everything here is wrapped so a benchmark problem (n8n, embeddings, its own tables) can only fail the
/// benchmark run itself — production Knowledge Base search never goes through this worker.
/// </summary>
public sealed class KnowledgeBenchmarkRunWorker : BackgroundService
{
    private readonly IKnowledgeBenchmarkRunQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KnowledgeBenchmarkRunWorker> _logger;

    public KnowledgeBenchmarkRunWorker(IKnowledgeBenchmarkRunQueue queue, IServiceScopeFactory scopes, ILogger<KnowledgeBenchmarkRunWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid runId;
            try
            {
                runId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IKnowledgeBenchmarkService>().ExecuteRunAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // ExecuteRunAsync records its own failures; this only guards the loop itself.
                _logger.LogError(ex, "Benchmark worker failed while running {RunId}.", runId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var pending = await scope.ServiceProvider.GetRequiredService<IKnowledgeBenchmarkService>().RecoverInterruptedRunsAsync(ct);
            foreach (var id in pending) _queue.Enqueue(id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // e.g. the benchmark tables aren't migrated yet — must never stop the app from starting.
            _logger.LogError(ex, "Could not recover benchmark run state at startup.");
        }
    }
}
