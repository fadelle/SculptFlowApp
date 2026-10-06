using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Leads;

namespace PlasticSurgery.Persistence.Repositories.Leads;

public class LeadRepository : ILeadRepository
{
    private readonly ApplicationDbContext _db;

    public LeadRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Lead?> GetAsync(Guid clinicId, Guid leadId, CancellationToken ct = default) =>
        _db.Leads.Include(l => l.Procedure).FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Id == leadId, ct);

    public Task<Lead?> GetByIdAsync(Guid leadId, CancellationToken ct = default) =>
        _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);

    public Task<Lead?> FindByExternalIdAsync(Guid clinicId, string externalLeadId, CancellationToken ct = default) =>
        _db.Leads.Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.ExternalLeadId == externalLeadId, ct);

    public Task<Lead?> FindByPhoneAsync(Guid clinicId, string phone, CancellationToken ct = default) =>
        _db.Leads.Include(l => l.Procedure).FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Phone == phone, ct);

    public async Task<(IReadOnlyList<Lead> Items, int TotalCount)> ListAsync(Guid clinicId, string? status, Guid? procedureId,
        string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Leads.Include(l => l.Procedure).Where(l => l.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (procedureId.HasValue)
        {
            query = query.Where(l => l.ProcedureId == procedureId);
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

    public async Task<IReadOnlyList<string>> ListDistinctSourcesAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.Leads
            .Where(l => l.ClinicId == clinicId && l.Source != null && l.Source != "")
            .Select(l => l.Source!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

    public void Add(Lead lead) => _db.Leads.Add(lead);

    public Task LoadProcedureAsync(Lead lead, CancellationToken ct = default) =>
        _db.Entry(lead).Reference(l => l.Procedure).LoadAsync(ct);

    public Task<List<Lead>> ListByIdsAsync(Guid clinicId, IReadOnlyCollection<Guid> leadIds, CancellationToken ct = default) =>
        _db.Leads.Where(l => l.ClinicId == clinicId && leadIds.Contains(l.Id)).ToListAsync(ct);

    public Task<string?> GetFullNameAsync(Guid leadId, CancellationToken ct = default) =>
        _db.Leads.Where(l => l.Id == leadId).Select(l => l.FullName).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(Guid clinicId, Guid leadId, CancellationToken ct = default) =>
        _db.Leads.AnyAsync(l => l.Id == leadId && l.ClinicId == clinicId, ct);
}
