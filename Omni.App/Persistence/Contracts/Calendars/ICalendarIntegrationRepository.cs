using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Calendars;

/// <summary>Google/Outlook calendar connections, their calendar lists and per-booking sync rows. Tracked.</summary>
public interface ICalendarIntegrationRepository
{
    void Add(CalendarIntegration integration);

    /// <summary>With Calendars loaded.</summary>
    Task<CalendarIntegration?> GetWithCalendarsAsync(Guid clientId, string provider, CancellationToken ct = default);


    /// <summary>Read-only copies of all of the client's connections, with Calendars loaded (cached).</summary>
    Task<IReadOnlyList<CalendarIntegration>> ListWithCalendarsReadOnlyAsync(Guid clientId, CancellationToken ct = default);
    Task<CalendarIntegration?> GetByIdAsync(Guid clientId, Guid integrationId, CancellationToken ct = default);

    /// <summary>Connected connections with sync on, a selected calendar and an access token.</summary>
    Task<IReadOnlyList<CalendarIntegration>> ListSyncTargetsAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Replaces the connection's stored calendar list.</summary>
    void ReplaceCalendars(CalendarIntegration integration, IEnumerable<CalendarIntegrationCalendar> calendars);

    void RemoveCalendars(CalendarIntegration integration);

    Task<BookingCalendarSync?> GetSyncAsync(Guid bookingId, Guid integrationId, CancellationToken ct = default);

    void AddSync(BookingCalendarSync sync);
}
