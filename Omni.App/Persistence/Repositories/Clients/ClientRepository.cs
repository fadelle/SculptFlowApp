using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.Clients;

public class ClientRepository : IClientRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public ClientRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public Task<Client?> GetAsync(Guid clientId, CancellationToken ct = default) =>
        _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);

    public Task<Client?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default) =>
        _cache.ReadThroughAsync(CacheKeys.Client(clientId), CacheArea.Clients,
            token => _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, token), ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        _db.Clients.AnyAsync(c => c.Slug == slug, ct);

    public void Add(Client client) => _db.Clients.Add(client);

    public void AddMembership(ClientUser membership) => _db.ClientUsers.Add(membership);

    public Task<UserAccess?> GetUserAccessAsync(string userId, CancellationToken ct = default) =>
        _cache.ReadThroughValueAsync(CacheKeys.Membership(userId), CacheArea.Memberships,
            async token => await (
                from u in _db.Users.AsNoTracking()
                where u.Id == userId && u.IsActive && (u.Role == UserRole.Client || u.Role == UserRole.Member)
                join cu in _db.ClientUsers.AsNoTracking() on u.Id equals cu.UserId
                where cu.IsActive
                orderby cu.CreatedAt
                select new UserAccess(u.Id, cu.ClientId, u.Role, u.Language, u.FullName, u.SecurityStamp)).FirstOrDefaultAsync(token), ct);

    public async Task<IReadOnlyList<ClientMemberRow>> ListMembersAsync(Guid clientId, CancellationToken ct = default) =>
        await (
            from cu in _db.ClientUsers.AsNoTracking()
            where cu.ClientId == clientId
            join u in _db.Users.AsNoTracking() on cu.UserId equals u.Id
            select new ClientMemberRow(cu.UserId, u.Email, u.FullName, u.Role, cu.IsActive && u.IsActive, cu.CreatedAt, u.LastLoginAt))
            .ToListAsync(ct);

    public Task<ClientUser?> GetMembershipAsync(Guid clientId, string userId, CancellationToken ct = default) =>
        _db.ClientUsers.FirstOrDefaultAsync(cu => cu.ClientId == clientId && cu.UserId == userId, ct);

    public Task<bool> HasOtherActiveMembershipAsync(string userId, Guid exceptMembershipId, CancellationToken ct = default) =>
        _db.ClientUsers.AnyAsync(cu => cu.UserId == userId && cu.IsActive && cu.Id != exceptMembershipId, ct);

    public Task<int> CountSeatsInUseAsync(Guid clientId, CancellationToken ct = default) =>
        (from cu in _db.ClientUsers
         where cu.ClientId == clientId && cu.IsActive
         join u in _db.Users on cu.UserId equals u.Id
         where u.IsActive && (u.Role == UserRole.Client || u.Role == UserRole.Member)
         select cu.Id).CountAsync(ct);

    public async Task<IReadOnlyList<ClientSummaryRow>> ListByNamePrefixAsync(string namePrefix, CancellationToken ct = default) =>
        await _db.Clients.AsNoTracking()
            .Where(c => c.Name.StartsWith(namePrefix))
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ClientSummaryRow(c.Id, c.Name, c.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListMemberUserIdsAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.ClientUsers.AsNoTracking()
            .Where(cu => cu.ClientId == clientId)
            .Select(cu => cu.UserId)
            .ToListAsync(ct);

    public async Task DeleteAsync(Guid clientId, CancellationToken ct = default)
    {
        await _db.Clients.Where(c => c.Id == clientId).ExecuteDeleteAsync(ct);
        await _cache.RemoveIfCachedAsync(CacheKeys.EvictionsForClient(clientId), ct);
    }

    public async Task<IReadOnlyList<ClientSummaryRow>> ListNamesAsync(CancellationToken ct = default) =>
        await _db.Clients.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new ClientSummaryRow(c.Id, c.Name, c.CreatedAt)).ToListAsync(ct);
}
