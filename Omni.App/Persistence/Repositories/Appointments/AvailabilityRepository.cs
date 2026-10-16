using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Persistence.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Appointments;

namespace Omni.App.Persistence.Repositories.Appointments;

public class AvailabilityRepository : IAvailabilityRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public AvailabilityRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<List<ClinicAvailabilityRule>> ListRulesAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicAvailabilityRules.Where(r => r.ClinicId == clinicId).ToListAsync(ct);

    public async Task<List<ClinicAvailabilityRule>> ListRulesReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        (await _cache.ReadThroughAsync(CacheKeys.AvailabilityRules(clinicId), CacheArea.Scheduling,
            async token => await _db.ClinicAvailabilityRules.AsNoTracking().Where(r => r.ClinicId == clinicId).ToListAsync(token),
            ct))!;

    public void AddRule(ClinicAvailabilityRule rule) => _db.ClinicAvailabilityRules.Add(rule);

    public Task<ClinicBookingSettings?> GetBookingSettingsAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicBookingSettings.FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public Task<ClinicBookingSettings?> GetBookingSettingsReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.BookingSettings(clinicId), CacheArea.Scheduling,
            token => _db.ClinicBookingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, token), ct);

    public void AddBookingSettings(ClinicBookingSettings settings) => _db.ClinicBookingSettings.Add(settings);

    public async Task<IReadOnlyList<ClinicAvailabilityException>> ListExceptionsAsync(Guid clinicId, DateOnly from, DateOnly to,
        CancellationToken ct = default) =>
        (await ListExceptionsReadOnlyAsync(clinicId, ct)).Where(e => e.Date >= from && e.Date <= to).ToList();

    public async Task<IReadOnlyList<ClinicAvailabilityException>> ListExceptionsFromAsync(Guid clinicId, DateOnly fromDate,
        CancellationToken ct = default) =>
        (await ListExceptionsReadOnlyAsync(clinicId, ct)).Where(e => e.Date >= fromDate).ToList();

    public Task<ClinicAvailabilityException?> GetExceptionByDateAsync(Guid clinicId, DateOnly date, CancellationToken ct = default) =>
        _db.ClinicAvailabilityExceptions.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Date == date, ct);

    public Task<ClinicAvailabilityException?> GetExceptionAsync(Guid clinicId, Guid exceptionId, CancellationToken ct = default) =>
        _db.ClinicAvailabilityExceptions.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == exceptionId, ct);

    public void AddException(ClinicAvailabilityException exception) => _db.ClinicAvailabilityExceptions.Add(exception);

    public void RemoveException(ClinicAvailabilityException exception) => _db.ClinicAvailabilityExceptions.Remove(exception);

    /// <summary>Every date exception of the clinic by date, read-only (cached; a clinic has a handful a year).</summary>
    private async Task<List<ClinicAvailabilityException>> ListExceptionsReadOnlyAsync(Guid clinicId, CancellationToken ct) =>
        (await _cache.ReadThroughAsync(CacheKeys.AvailabilityExceptions(clinicId), CacheArea.Scheduling,
            async token => await _db.ClinicAvailabilityExceptions.AsNoTracking().Where(e => e.ClinicId == clinicId)
                .OrderBy(e => e.Date).ToListAsync(token), ct))!;
}
