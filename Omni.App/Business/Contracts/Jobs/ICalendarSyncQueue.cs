using Omni.App.Entities.Requests.Calendars;

namespace Omni.App.Business.Contracts.Jobs;

/// <summary>Hands a calendar sync (Calendars / SyncEngine = code) to CalendarSyncWorker; never waits.</summary>
public interface ICalendarSyncQueue
{
    /// <summary>False only when the app is stopping.</summary>
    bool TryEnqueue(CalendarSyncTriggerPayload payload);

    ValueTask<CalendarSyncTriggerPayload> DequeueAsync(CancellationToken ct);

    void Complete();
}
