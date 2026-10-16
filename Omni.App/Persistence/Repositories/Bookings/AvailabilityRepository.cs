using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Bookings;

namespace Omni.App.Persistence.Repositories.Bookings;

public class AvailabilityRepository : IAvailabilityRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public AvailabilityRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<List<AvailabilityRule>> ListRulesAsync(Guid clientId, CancellationToken ct = default) =>
        _db.AvailabilityRules.Where(r => r.ClientId == clientId).ToListAsync(ct);

    public async Task<List<AvailabilityRule>> ListRulesReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.AvailabilityRules(clientId), CacheArea.Scheduling,
            async token => await _db.AvailabilityRules.AsNoTracking().Where(r => r.ClientId == clientId).ToListAsync(token),
            ct))!;

    public void AddRule(AvailabilityRule rule) => _db.AvailabilityRules.Add(rule);

    public Task<BookingSettings?> GetBookingSettingsAsync(Guid clientId, CancellationToken ct = default) =>
        _db.BookingSettings.FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public Task<BookingSettings?> GetBookingSettingsReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.BookingSettings(clientId), CacheArea.Scheduling,
            token => _db.BookingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == clientId, token), ct);

    public void AddBookingSettings(BookingSettings settings) => _db.BookingSettings.Add(settings);

    public async Task<IReadOnlyList<AvailabilityException>> ListExceptionsAsync(Guid clientId, DateOnly from, DateOnly to,
        CancellationToken ct = default) =>
        (await ListExceptionsReadOnlyAsync(clientId, ct)).Where(e => e.Date >= from && e.Date <= to).ToList();

    public async Task<IReadOnlyList<AvailabilityException>> ListExceptionsFromAsync(Guid clientId, DateOnly fromDate,
        CancellationToken ct = default) =>
        (await ListExceptionsReadOnlyAsync(clientId, ct)).Where(e => e.Date >= fromDate).ToList();

    public Task<AvailabilityException?> GetExceptionByDateAsync(Guid clientId, DateOnly date, CancellationToken ct = default) =>
        _db.AvailabilityExceptions.FirstOrDefaultAsync(x => x.ClientId == clientId && x.Date == date, ct);

    public Task<AvailabilityException?> GetExceptionAsync(Guid clientId, Guid exceptionId, CancellationToken ct = default) =>
        _db.AvailabilityExceptions.FirstOrDefaultAsync(x => x.ClientId == clientId && x.Id == exceptionId, ct);

    public void AddException(AvailabilityException exception) => _db.AvailabilityExceptions.Add(exception);

    public void RemoveException(AvailabilityException exception) => _db.AvailabilityExceptions.Remove(exception);

    /// <summary>Every date exception of the client by date, read-only (cached; a client has a handful a year).</summary>
    private async Task<List<AvailabilityException>> ListExceptionsReadOnlyAsync(Guid clientId, CancellationToken ct) =>
        (await _cache.ReadThroughAsync(CacheKeys.AvailabilityExceptions(clientId), CacheArea.Scheduling,
            async token => await _db.AvailabilityExceptions.AsNoTracking().Where(e => e.ClientId == clientId)
                .OrderBy(e => e.Date).ToListAsync(token), ct))!;
}
