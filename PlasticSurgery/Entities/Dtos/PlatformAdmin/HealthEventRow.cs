namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>One WhatsApp health event of a connection.</summary>
public record HealthEventRow(Guid Id, string EventType, string? Severity, string? Status, string? Code, string? Message,
    DateTimeOffset OccurredAt);
