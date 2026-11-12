using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Entities.Dtos.Knowledge;

namespace Omni.App.Business.Jobs;

public sealed class KnowledgeReindexQueue : IKnowledgeReindexQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, KnowledgeReindexProgress> _progress = new();

    public bool Start(Guid clientId, DateTimeOffset now)
    {
        var queued = new KnowledgeReindexProgress(0, 0, 0, now, null);
        while (true)
        {
            if (_progress.TryGetValue(clientId, out var current))
            {
                if (current.Running) return false;
                if (!_progress.TryUpdate(clientId, queued, current)) continue;
            }
            else if (!_progress.TryAdd(clientId, queued)) continue;
            return _channel.Writer.TryWrite(clientId);
        }
    }

    public KnowledgeReindexProgress? Progress(Guid clientId) => _progress.TryGetValue(clientId, out var p) ? p : null;

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    public bool TryDequeue([MaybeNullWhen(false)] out Guid clientId) => _channel.Reader.TryRead(out clientId);

    public void Report(Guid clientId, KnowledgeReindexProgress progress) => _progress[clientId] = progress;
}
