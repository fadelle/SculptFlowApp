namespace Omni.App.Entities.Models;

/// <summary>Maps to the "events" table. Named EventLog in C# to avoid colliding with the "event" keyword.</summary>
public class EventLog
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? BookingId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Source { get; set; }

    /// <summary>Raw JSON stored in the jsonb "metadata" column. Serialize/deserialize with System.Text.Json as needed.</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
