namespace PlasticSurgery.Entities.Models;

/// <summary>Maps to the "events" table. Named EventLog in C# to avoid colliding with the "event" keyword.</summary>
public class EventLog
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Source { get; set; }

    /// <summary>Raw JSON stored in the jsonb "metadata" column. Serialize/deserialize with System.Text.Json as needed.</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
