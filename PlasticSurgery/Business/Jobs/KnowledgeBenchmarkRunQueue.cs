using System.Threading.Channels;
using PlasticSurgery.Business.Contracts.Jobs;

namespace PlasticSurgery.Business.Jobs;

public sealed class KnowledgeBenchmarkRunQueue : IKnowledgeBenchmarkRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid runId) => _channel.Writer.TryWrite(runId);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
