namespace Omni.App.Business.Contracts.Managers;

/// <summary>Writes rows to the "events" table for analytics/debugging. Does not call SaveChanges —
/// the caller's SaveChangesAsync (usually inside the same service method) persists it together with
/// the business change, so an event is never logged for a write that didn't actually commit.</summary>
public interface IEventLogger
{
    void Log(Guid clientId, string eventType, Guid? contactId = null, Guid? conversationId = null,
        Guid? bookingId = null, string? source = null, string metadataJson = "{}");
}
