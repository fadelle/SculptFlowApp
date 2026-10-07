using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Responses.WhatsApp;

/// <summary>One row of WhatsApp health history for /WhatsApp/Health's event log.</summary>
public record WhatsAppHealthEventResponse(
    Guid Id,
    string EventType,
    string? Severity,
    string? Status,
    string? Code,
    string? Message,
    DateTimeOffset OccurredAt
);
