using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Persistence.Repositories.Contacts;

public class ContactRepository : IContactRepository
{
    private readonly ApplicationDbContext _db;

    public ContactRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Contact?> GetAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        _db.Contacts.Include(l => l.Service).FirstOrDefaultAsync(l => l.ClientId == clientId && l.Id == contactId, ct);

    public Task<Contact?> GetByIdAsync(Guid contactId, CancellationToken ct = default) =>
        _db.Contacts.FirstOrDefaultAsync(l => l.Id == contactId, ct);

    public Task<Contact?> FindByExternalIdAsync(Guid clientId, string externalContactId, CancellationToken ct = default) =>
        _db.Contacts.Include(l => l.Service)
            .FirstOrDefaultAsync(l => l.ClientId == clientId && l.ExternalContactId == externalContactId, ct);

    public Task<Contact?> FindByPhoneAsync(Guid clientId, string phone, CancellationToken ct = default) =>
        _db.Contacts.Include(l => l.Service).FirstOrDefaultAsync(l => l.ClientId == clientId && l.Phone == phone, ct);

    public async Task<(IReadOnlyList<Contact> Items, int TotalCount)> ListAsync(Guid clientId, string? stage, Guid? serviceId,
        string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Contacts.Include(l => l.Service).Where(l => l.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(stage))
        {
            query = query.Where(l => l.StageKey == stage);
        }

        if (serviceId.HasValue)
        {
            query = query.Where(l => l.ServiceId == serviceId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.FullName ?? "", pattern) ||
                EF.Functions.ILike(l.Phone ?? "", pattern) ||
                EF.Functions.ILike(l.Email ?? "", pattern));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStageAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.Contacts.Where(l => l.ClientId == clientId)
            .GroupBy(l => l.StageKey)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public Task<int> MoveStageAsync(Guid clientId, string fromKey, string toKey, DateTimeOffset now, CancellationToken ct = default) =>
        _db.Contacts.Where(l => l.ClientId == clientId && l.StageKey == fromKey)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.StageKey, toKey).SetProperty(l => l.UpdatedAt, now), ct);

    public async Task<IReadOnlyList<string>> ListDistinctSourcesAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.Contacts
            .Where(l => l.ClientId == clientId && l.Source != null && l.Source != "")
            .Select(l => l.Source!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

    public void Add(Contact contact) => _db.Contacts.Add(contact);

    public Task LoadServiceAsync(Contact contact, CancellationToken ct = default) =>
        _db.Entry(contact).Reference(l => l.Service).LoadAsync(ct);

    public Task<List<Contact>> ListByIdsAsync(Guid clientId, IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default) =>
        _db.Contacts.Where(l => l.ClientId == clientId && contactIds.Contains(l.Id)).ToListAsync(ct);

    public Task<string?> GetFullNameAsync(Guid contactId, CancellationToken ct = default) =>
        _db.Contacts.Where(l => l.Id == contactId).Select(l => l.FullName).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        _db.Contacts.AnyAsync(l => l.Id == contactId && l.ClientId == clientId, ct);
}
