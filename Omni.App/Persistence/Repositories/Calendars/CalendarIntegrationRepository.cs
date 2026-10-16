using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Calendars;

namespace Omni.App.Persistence.Repositories.Calendars;

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

    public Task<CalendarIntegration?> GetWithCalendarsAsync(Guid clientId, string provider, CancellationToken ct = default) =>
        _db.CalendarIntegrations.Include(c => c.Calendars)
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.Provider == provider, ct);

    public async Task<IReadOnlyList<CalendarIntegration>> ListWithCalendarsReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.Calendars(clientId), CacheArea.Scheduling,
            async token => await _db.CalendarIntegrations.AsNoTracking()
                .Include(c => c.Calendars)
                .Where(c => c.ClientId == clientId)
                .ToListAsync(token), ct))!;

    public Task<CalendarIntegration?> GetByIdAsync(Guid clientId, Guid integrationId, CancellationToken ct = default) =>
        _db.CalendarIntegrations.FirstOrDefaultAsync(c => c.Id == integrationId && c.ClientId == clientId, ct);

    public async Task<IReadOnlyList<CalendarIntegration>> ListSyncTargetsAsync(Guid clientId, CancellationToken ct = default)
    {
        // Runs on every booking change and most clients sync nothing: answer "none" from the cached list first.
        if (!(await ListWithCalendarsReadOnlyAsync(clientId, ct)).Any(IsSyncTarget)) return Array.Empty<CalendarIntegration>();

        return await _db.CalendarIntegrations
            .Where(c => c.ClientId == clientId && c.Status == CalendarIntegrationStatus.Connected && c.SyncEnabled
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

    public Task<BookingCalendarSync?> GetSyncAsync(Guid bookingId, Guid integrationId, CancellationToken ct = default) =>
        _db.BookingCalendarSyncs.FirstOrDefaultAsync(
            s => s.BookingId == bookingId && s.CalendarIntegrationId == integrationId, ct);

    public void AddSync(BookingCalendarSync sync) => _db.BookingCalendarSyncs.Add(sync);
}
