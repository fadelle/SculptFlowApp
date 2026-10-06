using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Appointments;

namespace PlasticSurgery.Persistence.Repositories.Appointments;

public class AvailabilityRepository : IAvailabilityRepository
{
    private readonly ApplicationDbContext _db;

    public AvailabilityRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<List<ClinicAvailabilityRule>> ListRulesAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicAvailabilityRules.Where(r => r.ClinicId == clinicId).ToListAsync(ct);

    public Task<List<ClinicAvailabilityRule>> ListRulesReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicAvailabilityRules.AsNoTracking().Where(r => r.ClinicId == clinicId).ToListAsync(ct);

    public void AddRule(ClinicAvailabilityRule rule) => _db.ClinicAvailabilityRules.Add(rule);

    public Task<ClinicBookingSettings?> GetBookingSettingsAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicBookingSettings.FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public Task<ClinicBookingSettings?> GetBookingSettingsReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.ClinicBookingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

    public void AddBookingSettings(ClinicBookingSettings settings) => _db.ClinicBookingSettings.Add(settings);

    public async Task<IReadOnlyList<ClinicAvailabilityException>> ListExceptionsAsync(Guid clinicId, DateOnly from, DateOnly to,
        CancellationToken ct = default) =>
        await _db.ClinicAvailabilityExceptions.AsNoTracking()
            .Where(e => e.ClinicId == clinicId && e.Date >= from && e.Date <= to)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClinicAvailabilityException>> ListExceptionsFromAsync(Guid clinicId, DateOnly fromDate,
        CancellationToken ct = default) =>
        await _db.ClinicAvailabilityExceptions
            .Where(e => e.ClinicId == clinicId && e.Date >= fromDate)
            .OrderBy(e => e.Date)
            .ToListAsync(ct);

    public Task<ClinicAvailabilityException?> GetExceptionByDateAsync(Guid clinicId, DateOnly date, CancellationToken ct = default) =>
        _db.ClinicAvailabilityExceptions.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Date == date, ct);

    public Task<ClinicAvailabilityException?> GetExceptionAsync(Guid clinicId, Guid exceptionId, CancellationToken ct = default) =>
        _db.ClinicAvailabilityExceptions.FirstOrDefaultAsync(x => x.ClinicId == clinicId && x.Id == exceptionId, ct);

    public void AddException(ClinicAvailabilityException exception) => _db.ClinicAvailabilityExceptions.Add(exception);

    public void RemoveException(ClinicAvailabilityException exception) => _db.ClinicAvailabilityExceptions.Remove(exception);
}
