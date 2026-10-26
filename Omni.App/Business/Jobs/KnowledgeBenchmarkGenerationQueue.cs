using System.Threading.Channels;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Entities.Dtos.KnowledgeBenchmark;

namespace Omni.App.Business.Jobs;

public sealed class KnowledgeBenchmarkGenerationQueue : IKnowledgeBenchmarkGenerationQueue
{
    private readonly Channel<BenchmarkGeneratorPayload> _channel =
        Channel.CreateUnbounded<BenchmarkGeneratorPayload>(new UnboundedChannelOptions { SingleReader = true });

    public bool TryEnqueue(BenchmarkGeneratorPayload payload) => _channel.Writer.TryWrite(payload);

    public ValueTask<BenchmarkGeneratorPayload> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);

    public void Complete() => _channel.Writer.TryComplete();
}
