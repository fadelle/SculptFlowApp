using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Dtos.Clinics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Persistence.Repositories.Clinics;

public class ClinicRepository : IClinicRepository
{
    private readonly ApplicationDbContext _db;

    public ClinicRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Clinic?> GetAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);

    public Task<Clinic?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        _db.Clinics.AnyAsync(c => c.Slug == slug, ct);

    public void Add(Clinic clinic) => _db.Clinics.Add(clinic);

    public void AddMembership(ClinicUser membership) => _db.ClinicUsers.Add(membership);

    public async Task<Clinic?> GetClinicOfActiveMemberAsync(string userId, CancellationToken ct = default)
    {
        var membership = await _db.ClinicUsers
            .Include(cu => cu.Clinic)
            .FirstOrDefaultAsync(cu => cu.UserId == userId && cu.IsActive, ct);
        return membership?.Clinic;
    }

    public async Task<Guid?> GetClinicIdOfActiveMemberAsync(string userId, CancellationToken ct = default)
    {
        var membership = await _db.ClinicUsers
            .Where(cu => cu.UserId == userId && cu.IsActive)
            .Select(cu => new { cu.ClinicId })
            .FirstOrDefaultAsync(ct);
        return membership?.ClinicId;
    }

    public async Task<IReadOnlyList<StaffMemberRow>> ListStaffAsync(Guid clinicId, string fullNameClaimType, CancellationToken ct = default) =>
        await (
            from cu in _db.ClinicUsers
            where cu.ClinicId == clinicId
            join u in _db.Users on cu.UserId equals u.Id
            select new StaffMemberRow(
                cu.UserId,
                u.Email,
                cu.IsActive,
                cu.CreatedAt,
                _db.UserClaims
                    .Where(c => c.UserId == u.Id && c.ClaimType == fullNameClaimType)
                    .Select(c => c.ClaimValue)
                    .FirstOrDefault())).ToListAsync(ct);

    public async Task<IReadOnlyList<ClinicSummaryRow>> ListByNamePrefixAsync(string namePrefix, CancellationToken ct = default) =>
        await _db.Clinics.AsNoTracking()
            .Where(c => c.Name.StartsWith(namePrefix))
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ClinicSummaryRow(c.Id, c.Name, c.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListMemberUserIdsAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.ClinicUsers.AsNoTracking()
            .Where(cu => cu.ClinicId == clinicId)
            .Select(cu => cu.UserId)
            .ToListAsync(ct);

    public Task DeleteAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Clinics.Where(c => c.Id == clinicId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<ClinicSummaryRow>> ListNamesAsync(CancellationToken ct = default) =>
        await _db.Clinics.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new ClinicSummaryRow(c.Id, c.Name, c.CreatedAt)).ToListAsync(ct);
}
