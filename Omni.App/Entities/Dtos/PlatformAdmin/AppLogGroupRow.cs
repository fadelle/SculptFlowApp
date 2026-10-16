namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>
/// One problem (fingerprint) over a period: how often it happened (<see cref="Occurrences"/> counts each row's repeat
/// count), when it was first and last seen, and its most recent row's text.
/// </summary>
public record AppLogGroupRow(
    string Fingerprint,
    string Level,
    string Category,
    string? MessageTemplate,
    string LatestMessage,
    string? ExceptionType,
    long Occurrences,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    long LatestId,
    Guid? LatestClientId);