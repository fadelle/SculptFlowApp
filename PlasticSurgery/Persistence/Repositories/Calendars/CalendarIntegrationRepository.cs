using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Calendars;

namespace PlasticSurgery.Persistence.Repositories.Calendars;

public class CalendarIntegrationRepository : ICalendarIntegrationRepository
{
    private readonly ApplicationDbContext _db;

    public CalendarIntegrationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(CalendarIntegration integration) => _db.CalendarIntegrations.Add(integration);

    public Task<CalendarIntegration?> GetWithCalendarsAsync(Guid clinicId, string provider, CancellationToken ct = default) =>
        _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);

    public async Task<IReadOnlyList<CalendarIntegration>> ListWithCalendarsAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.CalendarIntegrations
            .Include(c => c.Calendars)
            .Where(c => c.ClinicId == clinicId)
            .ToListAsync(ct);

    public Task<CalendarIntegration?> GetByIdAsync(Guid clinicId, Guid integrationId, CancellationToken ct = default) =>
        _db.CalendarIntegrations.FirstOrDefaultAsync(c => c.Id == integrationId && c.ClinicId == clinicId, ct);

    public async Task<IReadOnlyList<CalendarIntegration>> ListSyncTargetsAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.CalendarIntegrations
            .Where(c => c.ClinicId == clinicId && c.Status == CalendarIntegrationStatus.Connected && c.SyncEnabled
                        && c.SelectedCalendarId != null && c.AccessToken != null)
            .ToListAsync(ct);

    public void ReplaceCalendars(CalendarIntegration integration, IEnumerable<CalendarIntegrationCalendar> calendars)
    {
        _db.CalendarIntegrationCalendars.RemoveRange(integration.Calendars);
        _db.CalendarIntegrationCalendars.AddRange(calendars);
    }

    public void RemoveCalendars(CalendarIntegration integration) =>
        _db.CalendarIntegrationCalendars.RemoveRange(integration.Calendars);

    public Task<AppointmentCalendarSync?> GetSyncAsync(Guid appointmentId, Guid integrationId, CancellationToken ct = default) =>
        _db.AppointmentCalendarSyncs.FirstOrDefaultAsync(
            s => s.AppointmentId == appointmentId && s.CalendarIntegrationId == integrationId, ct);

    public void AddSync(AppointmentCalendarSync sync) => _db.AppointmentCalendarSyncs.Add(sync);
}
