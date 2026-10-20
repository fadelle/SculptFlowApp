using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.PlatformAdmin;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.PlatformAdmin;

public class MemberAdminRepository : IMemberAdminRepository
{
    private readonly ApplicationDbContext _db;

    public MemberAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<MemberRow>> ListAsync(Guid? clientId, string? search, int page, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = from u in _db.Users.AsNoTracking()
                where u.Role != UserRole.Admin
                join m in _db.ClientUsers.AsNoTracking() on u.Id equals m.UserId into ms
                from m in ms.DefaultIfEmpty()
                select new { u, m };
        if (clientId.HasValue) q = q.Where(x => x.m != null && x.m.ClientId == clientId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(x => (x.u.Email != null && EF.Functions.ILike(x.u.Email, like))
                             || (x.u.FullName != null && EF.Functions.ILike(x.u.FullName, like)));
        }

        return await q.OrderByDescending(x => x.m != null ? x.m.CreatedAt : DateTimeOffset.MinValue)
            .Select(x => new MemberRow(
                x.u.Id, x.u.Email, x.u.FullName,
                x.m != null ? x.m.Id : null,
                x.m != null ? x.m.ClientId : null,
                x.m != null ? _db.Clients.Where(c => c.Id == x.m.ClientId).Select(c => c.Name).FirstOrDefault() : null,
                x.m != null && x.m.IsActive,
                x.u.LockoutEnd != null && x.u.LockoutEnd > now,
                x.u.EmailConfirmed,
                x.u.AccessFailedCount,
                x.u.PasswordHash != null,
                _db.UserLogins.Any(l => l.UserId == x.u.Id),
                x.m != null ? x.m.CreatedAt : null,
                x.u.Role, x.u.IsActive, x.u.LastLoginAt, x.u.Language))
            .ToPagedAsync(page, ct: ct);
    }

    public async Task<MemberRow?> GetAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.Role != UserRole.Admin, ct);
        if (u is null) return null;
        var m = await _db.ClientUsers.AsNoTracking().Include(x => x.Client)
            .OrderByDescending(x => x.IsActive).FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var google = await _db.UserLogins.AnyAsync(l => l.UserId == userId, ct);
        return new MemberRow(u.Id, u.Email, u.FullName, m?.Id, m?.ClientId, m?.Client?.Name, m?.IsActive == true,
            u.LockoutEnd > now, u.EmailConfirmed, u.AccessFailedCount, u.PasswordHash != null, google, m?.CreatedAt,
            u.Role, u.IsActive, u.LastLoginAt, u.Language);
    }

    public Task<Guid?> ClientOfAsync(string userId, CancellationToken ct = default) =>
        _db.ClientUsers.Where(x => x.UserId == userId).OrderByDescending(x => x.IsActive)
            .Select(x => (Guid?)x.ClientId).FirstOrDefaultAsync(ct);

    /// <summary>Tracked: the user's most recently updated membership.</summary>
    public Task<ClientUser?> GetLatestMembershipAsync(string userId, CancellationToken ct = default) =>
        _db.ClientUsers.Where(x => x.UserId == userId).OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
}
