using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Persistence.Repositories.Knowledge;

public class WebsiteSourceRepository : IWebsiteSourceRepository
{
    private readonly ApplicationDbContext _db;

    public WebsiteSourceRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void AddSource(KnowledgeWebsiteSource source) => _db.KnowledgeWebsiteSources.Add(source);

    public void RemoveSource(KnowledgeWebsiteSource source) => _db.KnowledgeWebsiteSources.Remove(source); // pages + runs cascade

    public Task<int> CountSourcesAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteSources.CountAsync(s => s.ClinicId == clinicId, ct);

    public Task<bool> SourceUrlExistsAsync(Guid clinicId, string normalizedStartUrl, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteSources.AnyAsync(s => s.ClinicId == clinicId && s.NormalizedStartUrl == normalizedStartUrl, ct);

    public async Task<IReadOnlyList<KnowledgeWebsiteSource>> ListSourcesReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeWebsiteSources.AsNoTracking()
            .Where(s => s.ClinicId == clinicId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);

    public Task<KnowledgeWebsiteSource?> GetSourceReadOnlyAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteSources.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Id == sourceId, ct);

    public Task<KnowledgeWebsiteSource?> GetSourceAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Id == sourceId, ct);

    public Task<KnowledgeWebsiteSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);

    public Task ReloadAsync(KnowledgeWebsiteSource source, CancellationToken ct = default) => _db.Entry(source).ReloadAsync(ct);

    public void AddRun(KnowledgeWebsiteScrapeRun run) => _db.KnowledgeWebsiteScrapeRuns.Add(run);

    public Task<KnowledgeWebsiteScrapeRun?> GetRunAsync(Guid runId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteScrapeRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);

    public async Task<IReadOnlyList<KnowledgeWebsiteScrapeRun>> ListRunsReadOnlyAsync(Guid clinicId, IReadOnlyCollection<Guid> sourceIds,
        CancellationToken ct = default) =>
        await _db.KnowledgeWebsiteScrapeRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && sourceIds.Contains(r.WebsiteSourceId)).ToListAsync(ct);

    public async Task<IReadOnlyList<KnowledgeWebsiteScrapeRun>> ListRecentRunsReadOnlyAsync(Guid clinicId, Guid sourceId, int take,
        CancellationToken ct = default) =>
        await _db.KnowledgeWebsiteScrapeRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.WebsiteSourceId == sourceId)
            .OrderByDescending(r => r.CreatedAt).Take(take).ToListAsync(ct);

    public Task<KnowledgeWebsiteScrapeRun?> GetLatestRunReadOnlyAsync(Guid sourceId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteScrapeRuns.AsNoTracking().Where(r => r.WebsiteSourceId == sourceId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);

    public Task<bool> HasRunInProgressAsync(Guid sourceId, CancellationToken ct = default) =>
        _db.KnowledgeWebsiteScrapeRuns.AnyAsync(r => r.WebsiteSourceId == sourceId
            && (r.Status == WebsiteScrapeStatus.Pending || r.Status == WebsiteScrapeStatus.Crawling), ct);

    public async Task<IReadOnlyList<KnowledgeWebsiteScrapeRun>> ListRunsInStatusAsync(string status, CancellationToken ct = default) =>
        await _db.KnowledgeWebsiteScrapeRuns.Where(r => r.Status == status).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListRunIdsInStatusAsync(string status, CancellationToken ct = default) =>
        await _db.KnowledgeWebsiteScrapeRuns.Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct);

    public void AddPage(KnowledgeWebsitePage page) => _db.KnowledgeWebsitePages.Add(page);

    public Task<Dictionary<string, KnowledgeWebsitePage>> MapPagesByUrlAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default) =>
        _db.KnowledgeWebsitePages
            .Where(p => p.WebsiteSourceId == sourceId && p.ClinicId == clinicId)
            .ToDictionaryAsync(p => p.NormalizedUrl, StringComparer.Ordinal, ct);

    public async Task<IReadOnlyList<KnowledgeWebsitePage>> ListPagesReadOnlyAsync(Guid clinicId, Guid sourceId, string? status,
        int skip, int take, CancellationToken ct = default)
    {
        var query = _db.KnowledgeWebsitePages.AsNoTracking().Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);

        return await query.OrderBy(p => p.Depth).ThenBy(p => p.NormalizedUrl).Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountPagesByStatusAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default) =>
        await _db.KnowledgeWebsitePages.AsNoTracking()
            .Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId)
            .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountIndexedPagesAsync(Guid clinicId, IReadOnlyCollection<Guid> sourceIds,
        CancellationToken ct = default) =>
        await _db.KnowledgeWebsitePages.AsNoTracking()
            .Where(p => p.ClinicId == clinicId && sourceIds.Contains(p.WebsiteSourceId)
                        && (p.Status == WebsitePageStatus.Indexed || p.Status == WebsitePageStatus.Unchanged))
            .GroupBy(p => p.WebsiteSourceId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

    public async Task<IReadOnlyList<Guid>> ListPageDocumentIdsAsync(Guid clinicId, Guid sourceId, bool onlyInKnowledgeBase,
        CancellationToken ct = default) =>
        await _db.KnowledgeWebsitePages
            .Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId && p.KnowledgeDocumentId != null
                        && (!onlyInKnowledgeBase || p.Status == WebsitePageStatus.Indexed || p.Status == WebsitePageStatus.Unchanged))
            .Select(p => p.KnowledgeDocumentId!.Value).ToListAsync(ct);
}
