using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>One clinic's connection to an external calendar provider (Google or Outlook). SculptFlow owns the OAuth
/// relationship directly (connect/disconnect/list-calendars) and stores the resulting tokens here; n8n is only handed
/// a fresh AccessToken (via ICalendarSyncNotifier.NotifySyncAsync) to execute the actual appointment sync call.
/// MVP NOTE: tokens are stored in plain text, same caveat as ChannelIntegration.AccessToken — move to an encrypted
/// column/secrets manager before this handles real patient data at scale.</summary>
public class CalendarIntegration
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = CalendarIntegrationStatus.Disconnected;

    public string? ExternalConnectionRef { get; set; }
    public string? AccountDisplayName { get; set; }

    public string? SelectedCalendarId { get; set; }
    public string? SelectedCalendarName { get; set; }

    public bool SyncEnabled { get; set; }

    public bool IsHealthy { get; set; } = true;
    public string? LastProblemMessage { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<CalendarIntegrationCalendar> Calendars { get; set; } = new List<CalendarIntegrationCalendar>();
}
