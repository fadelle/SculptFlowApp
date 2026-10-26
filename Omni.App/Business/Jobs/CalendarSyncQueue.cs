using System.Threading.Channels;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Entities.Requests.Calendars;

namespace Omni.App.Business.Jobs;

public sealed class CalendarSyncQueue : ICalendarSyncQueue
{
    private readonly Channel<CalendarSyncTriggerPayload> _channel =
        Channel.CreateUnbounded<CalendarSyncTriggerPayload>(new UnboundedChannelOptions { SingleReader = true });

    public bool TryEnqueue(CalendarSyncTriggerPayload payload) => _channel.Writer.TryWrite(payload);

    public ValueTask<CalendarSyncTriggerPayload> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);

    public void Complete() => _channel.Writer.TryComplete();
}
