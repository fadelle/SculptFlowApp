namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>One activity.app_logs row in a list (no stack trace or properties: see <see cref="AppLogDetail"/>).</summary>
public record AppLogRow(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string Category,
    string Message,
    string? ExceptionType,
    string Fingerprint,
    int RepeatCount,
    string? TraceId,
    Guid? ClinicId,
    Guid? ConversationId,
    Guid? MessageId,
    string? Method,
    string? Route);