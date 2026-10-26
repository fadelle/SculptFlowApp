using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Jobs;

public sealed class BotRunQueue : IBotRunQueue
{
    private readonly Channel<AiTriggerPayload> _channel = Channel.CreateUnbounded<AiTriggerPayload>(new UnboundedChannelOptions { SingleReader = true });

    public bool TryEnqueue(AiTriggerPayload trigger) => _channel.Writer.TryWrite(trigger);

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    public bool TryDequeue([MaybeNullWhen(false)] out AiTriggerPayload trigger) => _channel.Reader.TryRead(out trigger);

    public void Complete() => _channel.Writer.TryComplete();
}
