using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Services.Knowledge;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Runs queued Knowledge Base re-indexes (IKnowledgeReindexQueue, QA t4-ai-15) one client at a time, one entry at a time, each
/// entry in its own DI scope (IKnowledgeService.ReindexAsync: re-chunk and re-embed the stored text; a provider failure keeps
/// that entry's old chunks). Progress is reported after every entry; an entry that fails is counted and logged, and the rest go on.
/// </summary>
public sealed class KnowledgeReindexWorker : BackgroundService
{
    private readonly IKnowledgeReindexQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<KnowledgeReindexWorker> _logger;

    public KnowledgeReindexWorker(IKnowledgeReindexQueue queue, IServiceScopeFactory scopes, TimeProvider time, ILogger<KnowledgeReindexWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _queue.WaitToReadAsync(stoppingToken))
            {
                while (_queue.TryDequeue(out var clientId)) await RunAsync(clientId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // stopping
        }
    }

    internal async Task RunAsync(Guid clientId, CancellationToken ct)
    {
        var started = _queue.Progress(clientId)?.StartedAt ?? _time.GetUtcNow();
        IReadOnlyList<Guid> ids;
        try
        {
            await using var listScope = _scopes.CreateAsyncScope();
            ids = (await listScope.ServiceProvider.GetRequiredService<IKnowledgeService>().ListAsync(clientId, ct)).Select(d => d.Id).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Couldn't list the Knowledge Base of client {ClientId} to re-index it.", clientId);
            _queue.Report(clientId, new(0, 0, 0, started, _time.GetUtcNow()));
            return;
        }

        int done = 0, failed = 0;
        _queue.Report(clientId, new(ids.Count, 0, 0, started, null));
        foreach (var id in ids)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<IKnowledgeService>().ReindexAsync(clientId, id, ct)) failed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _queue.Report(clientId, new(ids.Count, done, failed, started, _time.GetUtcNow()));
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Re-indexing knowledge entry {DocumentId} of client {ClientId} failed; its old chunks stay.", id, clientId);
            }
            done++;
            _queue.Report(clientId, new(ids.Count, done, failed, started, done == ids.Count ? _time.GetUtcNow() : null));
        }
        if (ids.Count == 0) _queue.Report(clientId, new(0, 0, 0, started, _time.GetUtcNow()));
        _logger.LogInformation("Re-indexed the Knowledge Base of client {ClientId}: {Done} entries, {Failed} failed.", clientId, done, failed);
    }
}
