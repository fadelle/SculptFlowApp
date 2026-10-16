namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One activity.app_logs row in full: exception text, stack trace and the logged values (secrets already masked).</summary>
public record AppLogDetail(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string Category,
    string Message,
    string? MessageTemplate,
    string? ExceptionType,
    string? ExceptionMessage,
    string? StackTrace,
    string Fingerprint,
    int RepeatCount,
    string? TraceId,
    Guid? ClinicId,
    Guid? ConversationId,
    Guid? MessageId,
    string? Method,
    string? Route,
    string? Instance,
    Dictionary<string, string?> Properties);