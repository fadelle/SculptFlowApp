using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Leads;

namespace Omni.App.Persistence.Repositories.Leads;

public class LeadRepository : ILeadRepository
{
    private readonly ApplicationDbContext _db;

    public LeadRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Lead?> GetAsync(Guid clientId, Guid leadId, CancellationToken ct = default) =>
        _db.Leads.Include(l => l.Service).FirstOrDefaultAsync(l => l.ClientId == clientId && l.Id == leadId, ct);

    public Task<Lead?> GetByIdAsync(Guid leadId, CancellationToken ct = default) =>
        _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);

    public Task<Lead?> FindByExternalIdAsync(Guid clientId, string externalLeadId, CancellationToken ct = default) =>
        _db.Leads.Include(l => l.Service)
            .FirstOrDefaultAsync(l => l.ClientId == clientId && l.ExternalLeadId == externalLeadId, ct);

    public Task<Lead?> FindByPhoneAsync(Guid clientId, string phone, CancellationToken ct = default) =>
        _db.Leads.Include(l => l.Service).FirstOrDefaultAsync(l => l.ClientId == clientId && l.Phone == phone, ct);

    public async Task<(IReadOnlyList<Lead> Items, int TotalCount)> ListAsync(Guid clientId, string? status, Guid? serviceId,
        string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Leads.Include(l => l.Service).Where(l => l.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(l => l.Status == status);
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

    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.Leads.Where(l => l.ClientId == clientId)
            .GroupBy(l => l.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public Task<int> MoveStatusAsync(Guid clientId, string fromStatus, string toStatus, DateTimeOffset now, CancellationToken ct = default) =>
        _db.Leads.Where(l => l.ClientId == clientId && l.Status == fromStatus)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, toStatus).SetProperty(l => l.UpdatedAt, now), ct);

    public async Task<IReadOnlyList<string>> ListDistinctSourcesAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.Leads
            .Where(l => l.ClientId == clientId && l.Source != null && l.Source != "")
            .Select(l => l.Source!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

    public void Add(Lead lead) => _db.Leads.Add(lead);

    public Task LoadServiceAsync(Lead lead, CancellationToken ct = default) =>
        _db.Entry(lead).Reference(l => l.Service).LoadAsync(ct);

    public Task<List<Lead>> ListByIdsAsync(Guid clientId, IReadOnlyCollection<Guid> leadIds, CancellationToken ct = default) =>
        _db.Leads.Where(l => l.ClientId == clientId && leadIds.Contains(l.Id)).ToListAsync(ct);

    public Task<string?> GetFullNameAsync(Guid leadId, CancellationToken ct = default) =>
        _db.Leads.Where(l => l.Id == leadId).Select(l => l.FullName).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(Guid clientId, Guid leadId, CancellationToken ct = default) =>
        _db.Leads.AnyAsync(l => l.Id == leadId && l.ClientId == clientId, ct);
}
