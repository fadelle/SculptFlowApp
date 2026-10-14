using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Calendars;

namespace PlasticSurgery.Persistence.Repositories.Calendars;

public class CalendarIntegrationRepository : ICalendarIntegrationRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public CalendarIntegrationRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public void Add(CalendarIntegration integration) => _db.CalendarIntegrations.Add(integration);

    public Task<CalendarIntegration?> GetWithCalendarsAsync(Guid clinicId, string provider, CancellationToken ct = default) =>
        _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Provider == provider, ct);

    public async Task<IReadOnlyList<CalendarIntegration>> ListWithCalendarsReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.Calendars(clinicId), CacheArea.Scheduling,
            async token => await _db.CalendarIntegrations.AsNoTracking()
                .Include(c => c.Calendars)
                .Where(c => c.ClinicId == clinicId)
                .ToListAsync(token), ct))!;

    public Task<CalendarIntegration?> GetByIdAsync(Guid clinicId, Guid integrationId, CancellationToken ct = default) =>
        _db.CalendarIntegrations.FirstOrDefaultAsync(c => c.Id == integrationId && c.ClinicId == clinicId, ct);

    public async Task<IReadOnlyList<CalendarIntegration>> ListSyncTargetsAsync(Guid clinicId, CancellationToken ct = default)
    {
        // Runs on every appointment change and most clinics sync nothing: answer "none" from the cached list first.
        if (!(await ListWithCalendarsReadOnlyAsync(clinicId, ct)).Any(IsSyncTarget)) return Array.Empty<CalendarIntegration>();

        return await _db.CalendarIntegrations
            .Where(c => c.ClinicId == clinicId && c.Status == CalendarIntegrationStatus.Connected && c.SyncEnabled
                        && c.SelectedCalendarId != null && c.AccessToken != null)
            .ToListAsync(ct);
    }

    private static bool IsSyncTarget(CalendarIntegration c) =>
        c.Status == CalendarIntegrationStatus.Connected && c.SyncEnabled && c.SelectedCalendarId != null && c.AccessToken != null;

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
