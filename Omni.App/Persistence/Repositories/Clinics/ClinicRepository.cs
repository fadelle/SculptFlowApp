using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clinics;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Clinics;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.Clinics;

public class ClinicRepository : IClinicRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ClinicRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<Clinic?> GetAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);

    public Task<Clinic?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        _cache.ReadThroughAsync(CacheKeys.Clinic(clinicId), CacheArea.Clinics,
            token => _db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, token), ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        _db.Clinics.AnyAsync(c => c.Slug == slug, ct);

    public void Add(Clinic clinic) => _db.Clinics.Add(clinic);

    public void AddMembership(ClinicUser membership) => _db.ClinicUsers.Add(membership);

    public async Task<Clinic?> GetClinicOfActiveMemberAsync(string userId, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdOfActiveMemberAsync(userId, ct);
        return clinicId is null ? null : await GetReadOnlyAsync(clinicId.Value, ct);
    }

    public Task<Guid?> GetClinicIdOfActiveMemberAsync(string userId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.Membership(userId), CacheArea.StaffLookup,
            async token => (await _db.ClinicUsers.AsNoTracking()
                .Where(cu => cu.UserId == userId && cu.IsActive)
                .Select(cu => new { cu.ClinicId })
                .FirstOrDefaultAsync(token))?.ClinicId, ct);

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

    public async Task DeleteAsync(Guid clinicId, CancellationToken ct = default)
    {
        await _db.Clinics.Where(c => c.Id == clinicId).ExecuteDeleteAsync(ct);
        await _cache.RemoveIfCachedAsync(CacheKeys.EvictionsForClinic(clinicId), ct);
    }

    public async Task<IReadOnlyList<ClinicSummaryRow>> ListNamesAsync(CancellationToken ct = default) =>
        await _db.Clinics.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new ClinicSummaryRow(c.Id, c.Name, c.CreatedAt)).ToListAsync(ct);
}
