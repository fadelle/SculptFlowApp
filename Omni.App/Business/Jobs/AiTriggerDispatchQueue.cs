using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Jobs;

/// <summary>In memory, like BotRunQueue: a trigger still waiting when the app stops is lost (the customer's next message
/// triggers again). Bounded, so a stuck n8n can't grow it without limit; when full the webhook triggers inline.</summary>
public sealed class AiTriggerDispatchQueue : IAiTriggerDispatchQueue
{
    public const int Capacity = 2000;

    private readonly Channel<AiTriggerPayload> _channel = Channel.CreateBounded<AiTriggerPayload>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(AiTriggerPayload trigger) => _channel.Writer.TryWrite(trigger);

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);

    public bool TryDequeue([MaybeNullWhen(false)] out AiTriggerPayload trigger) => _channel.Reader.TryRead(out trigger);
}
