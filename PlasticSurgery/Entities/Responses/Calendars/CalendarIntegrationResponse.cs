using PlasticSurgery.Entities.Dtos.Calendars;

namespace PlasticSurgery.Entities.Responses.Calendars;

/// <summary>One provider's card on the Calendar Integrations page. Always returned even if the clinic never connected
/// this provider (Id = Guid.Empty, Status = disconnected) — same convention as ChannelIntegrationResponse, so the
/// page never has to special-case "no row yet".</summary>
public record CalendarIntegrationResponse(
    Guid Id,
    Guid ClinicId,
    string Provider,
    string Status,
    string? AccountDisplayName,
    string? SelectedCalendarId,
    string? SelectedCalendarName,
    bool SyncEnabled,
    bool IsHealthy,
    string? LastProblemMessage,
    DateTimeOffset? LastSyncedAt,
    IReadOnlyList<CalendarCalendarOption> AvailableCalendars
);
