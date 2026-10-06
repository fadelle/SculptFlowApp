using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Calendars;

/// <summary>Google/Outlook calendar connections, their calendar lists and per-appointment sync rows. Tracked.</summary>
public interface ICalendarIntegrationRepository
{
    void Add(CalendarIntegration integration);

    /// <summary>With Calendars loaded.</summary>
    Task<CalendarIntegration?> GetWithCalendarsAsync(Guid clinicId, string provider, CancellationToken ct = default);

    /// <summary>All of the clinic's connections, with Calendars loaded.</summary>
    Task<IReadOnlyList<CalendarIntegration>> ListWithCalendarsAsync(Guid clinicId, CancellationToken ct = default);

    Task<CalendarIntegration?> GetByIdAsync(Guid clinicId, Guid integrationId, CancellationToken ct = default);

    /// <summary>Connected connections with sync on, a selected calendar and an access token.</summary>
    Task<IReadOnlyList<CalendarIntegration>> ListSyncTargetsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Replaces the connection's stored calendar list.</summary>
    void ReplaceCalendars(CalendarIntegration integration, IEnumerable<CalendarIntegrationCalendar> calendars);

    void RemoveCalendars(CalendarIntegration integration);

    Task<AppointmentCalendarSync?> GetSyncAsync(Guid appointmentId, Guid integrationId, CancellationToken ct = default);

    void AddSync(AppointmentCalendarSync sync);
}
