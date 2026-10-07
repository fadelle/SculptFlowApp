using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;
using PlasticSurgery.Persistence.Helpers;

namespace PlasticSurgery.Persistence.Repositories.PlatformAdmin;

public class StaffAdminRepository : IStaffAdminRepository
{
    private readonly ApplicationDbContext _db;

    public StaffAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<StaffRow>> ListAsync(Guid? clinicId, string? search, int page, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = from u in _db.Users.AsNoTracking()
                join m in _db.ClinicUsers.AsNoTracking() on u.Id equals m.UserId into ms
                from m in ms.DefaultIfEmpty()
                select new { u, m };
        if (clinicId.HasValue) q = q.Where(x => x.m != null && x.m.ClinicId == clinicId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(x => (x.u.Email != null && EF.Functions.ILike(x.u.Email, like))
                             || _db.UserClaims.Any(c => c.UserId == x.u.Id && c.ClaimType == StaffClaims.FullName
                                                        && c.ClaimValue != null && EF.Functions.ILike(c.ClaimValue, like)));
        }

        return await q.OrderByDescending(x => x.m != null ? x.m.CreatedAt : DateTimeOffset.MinValue)
            .Select(x => new StaffRow(
                x.u.Id, x.u.Email,
                _db.UserClaims.Where(c => c.UserId == x.u.Id && c.ClaimType == StaffClaims.FullName).Select(c => c.ClaimValue).FirstOrDefault(),
                x.m != null ? x.m.Id : null,
                x.m != null ? x.m.ClinicId : null,
                x.m != null ? _db.Clinics.Where(c => c.Id == x.m.ClinicId).Select(c => c.Name).FirstOrDefault() : null,
                x.m != null && x.m.IsActive,
                x.u.LockoutEnd != null && x.u.LockoutEnd > now,
                x.u.EmailConfirmed,
                x.u.AccessFailedCount,
                x.u.PasswordHash != null,
                _db.UserLogins.Any(l => l.UserId == x.u.Id),
                x.m != null ? x.m.CreatedAt : null))
            .ToPagedAsync(page, ct: ct);
    }

    public async Task<StaffRow?> GetAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return null;
        var m = await _db.ClinicUsers.AsNoTracking().Include(x => x.Clinic)
            .OrderByDescending(x => x.IsActive).FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var name = await _db.UserClaims.Where(c => c.UserId == userId && c.ClaimType == StaffClaims.FullName)
            .Select(c => c.ClaimValue).FirstOrDefaultAsync(ct);
        var google = await _db.UserLogins.AnyAsync(l => l.UserId == userId, ct);
        return new StaffRow(u.Id, u.Email, name, m?.Id, m?.ClinicId, m?.Clinic?.Name, m?.IsActive == true,
            u.LockoutEnd > now, u.EmailConfirmed, u.AccessFailedCount, u.PasswordHash != null, google, m?.CreatedAt);
    }

    public Task<Guid?> ClinicOfAsync(string userId, CancellationToken ct = default) =>
        _db.ClinicUsers.Where(x => x.UserId == userId).OrderByDescending(x => x.IsActive)
            .Select(x => (Guid?)x.ClinicId).FirstOrDefaultAsync(ct);

    /// <summary>Tracked: the user's most recently updated membership.</summary>
    public Task<ClinicUser?> GetLatestMembershipAsync(string userId, CancellationToken ct = default) =>
        _db.ClinicUsers.Where(x => x.UserId == userId).OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);

    public Task<bool> HasOtherActiveMembershipAsync(string userId, Guid exceptMembershipId, CancellationToken ct = default) =>
        _db.ClinicUsers.AnyAsync(x => x.UserId == userId && x.IsActive && x.Id != exceptMembershipId, ct);
}
