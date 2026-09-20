using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>
/// What the dashboard does with website sources: add one (and start its first crawl), list/inspect, re-scrape,
/// activate/deactivate, delete. It only manages rows and queues work — the crawling itself is
/// <see cref="IWebsiteScrapeProcessor"/>'s job, run in the background. EVERY method takes the clinicId the
/// caller resolved from CurrentClinicContext and scopes every query by it.
/// Throws ArgumentException (staff-readable message) for invalid input or a refused action.
/// </summary>
public interface IWebsiteSourceService
{
    Task<WebsiteSourceResponse> CreateAsync(Guid clinicId, CreateWebsiteSourceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WebsiteSourceResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);
    Task<WebsiteSourceDetailResponse?> GetAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<WebsitePageResponse>> ListPagesAsync(Guid clinicId, Guid sourceId, string? status, int skip, int take, CancellationToken ct = default);
    /// <summary>Starts another crawl of the same website. Null if the source isn't in this clinic.</summary>
    Task<WebsiteSourceResponse?> RescrapeAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);
    /// <summary>Deactivating hides every page document of the website from AI search; activating restores them.</summary>
    Task<WebsiteSourceResponse?> SetActiveAsync(Guid clinicId, Guid sourceId, bool isActive, CancellationToken ct = default);
    /// <summary>Deletes the source, its page/run records AND the knowledge documents (with their chunks) that
    /// the crawl created. Manual entries, uploads, other websites and other clinics are untouched.</summary>
    Task<bool> DeleteAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);
}

public sealed class WebsiteSourceService : IWebsiteSourceService
{
    private readonly ApplicationDbContext _db;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteScrapeQueue _queue;
    private readonly SsrfGuard _guard;
    private readonly WebsiteScrapeOptions _options;

    public WebsiteSourceService(
        ApplicationDbContext db, IKnowledgeService knowledge, IWebsiteScrapeQueue queue, SsrfGuard guard, WebsiteScrapeOptions options)
    {
        _db = db;
        _knowledge = knowledge;
        _queue = queue;
        _guard = guard;
        _options = options;
    }

    public async Task<WebsiteSourceResponse> CreateAsync(Guid clinicId, CreateWebsiteSourceRequest request, CancellationToken ct = default)
    {
        var raw = (request.Url ?? string.Empty).Trim();
        if (raw.Length == 0) throw new ArgumentException("Enter the website address to import.");
        // Be forgiving: "clinic.com" → https://clinic.com
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;

        if (!UrlNormalizer.TryNormalize(raw, null, out var normalized))
        {
            throw new ArgumentException("That doesn't look like a valid web address. Use a full http:// or https:// URL.");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("That doesn't look like a valid web address. Use a full http:// or https:// URL.");
        }
        try
        {
            // Refuse localhost / private / internal destinations up front, with a clear message.
            await _guard.ValidateUrlAsync(uri, ct);
        }
        catch (UnsafeUrlException ex)
        {
            throw new ArgumentException(ex.Message);
        }

        var mode = string.IsNullOrWhiteSpace(request.CrawlMode) ? WebsiteCrawlMode.CrawlSite : request.CrawlMode.Trim().ToLowerInvariant();
        if (!WebsiteCrawlMode.IsValid(mode)) throw new ArgumentException("Choose either \"Single page only\" or \"Crawl website\".");

        var category = string.IsNullOrWhiteSpace(request.Category) ? KnowledgeCategory.General : request.Category.Trim().ToLowerInvariant();
        if (category.Length > 50) throw new ArgumentException("Category must be 50 characters or fewer.");

        if (await _db.KnowledgeWebsiteSources.CountAsync(s => s.ClinicId == clinicId, ct) >= _options.MaxSourcesPerClinic)
        {
            throw new ArgumentException($"You can add up to {_options.MaxSourcesPerClinic} websites. Delete one you no longer need first.");
        }
        if (await _db.KnowledgeWebsiteSources.AnyAsync(s => s.ClinicId == clinicId && s.NormalizedStartUrl == normalized, ct))
        {
            throw new ArgumentException("This website address has already been added. Open it and use \"Re-scrape\" to refresh it.");
        }

        var now = DateTimeOffset.UtcNow;
        var source = new KnowledgeWebsiteSource
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            StartUrl = raw.Length > 2000 ? raw[..2000] : raw,
            NormalizedStartUrl = normalized,
            Host = uri.IdnHost,
            CrawlMode = mode,
            Category = category,
            IsActive = request.IsActive,
            Status = WebsiteScrapeStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        var run = NewRun(source);
        _db.KnowledgeWebsiteSources.Add(source);
        _db.KnowledgeWebsiteScrapeRuns.Add(run);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new ArgumentException("This website address has already been added.");
        }

        _queue.Enqueue(run.Id); // after the commit, so the worker can always find the run
        return ToResponse(source, run, 0);
    }

    public async Task<IReadOnlyList<WebsiteSourceResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var sources = await _db.KnowledgeWebsiteSources.AsNoTracking()
            .Where(s => s.ClinicId == clinicId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var ids = sources.Select(s => s.Id).ToList();

        var runs = await _db.KnowledgeWebsiteScrapeRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && ids.Contains(r.WebsiteSourceId)).ToListAsync(ct);
        var latest = runs.GroupBy(r => r.WebsiteSourceId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());

        var indexed = await IndexedCountsAsync(clinicId, ids, ct);
        return sources.Select(s => ToResponse(s, latest.GetValueOrDefault(s.Id), indexed.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<WebsiteSourceDetailResponse?> GetAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _db.KnowledgeWebsiteSources.AsNoTracking().FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Id == sourceId, ct);
        if (source is null) return null;

        var runs = await _db.KnowledgeWebsiteScrapeRuns.AsNoTracking()
            .Where(r => r.ClinicId == clinicId && r.WebsiteSourceId == sourceId)
            .OrderByDescending(r => r.CreatedAt).Take(8).ToListAsync(ct);

        var counts = await _db.KnowledgeWebsitePages.AsNoTracking()
            .Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId)
            .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var indexed = (await IndexedCountsAsync(clinicId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId);
        return new WebsiteSourceDetailResponse(
            ToResponse(source, runs.FirstOrDefault(), indexed), runs.Select(ToRunResponse).ToList(), counts);
    }

    public async Task<IReadOnlyList<WebsitePageResponse>> ListPagesAsync(
        Guid clinicId, Guid sourceId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.KnowledgeWebsitePages.AsNoTracking().Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);

        return (await query.OrderBy(p => p.Depth).ThenBy(p => p.NormalizedUrl)
                .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct))
            .Select(p => new WebsitePageResponse(p.Id, p.NormalizedUrl, p.Title, p.Status, p.HttpStatus, p.FailureReason,
                p.CanonicalUrl, p.KnowledgeDocumentId, p.Depth, p.LastScrapedAt))
            .ToList();
    }

    public async Task<WebsiteSourceResponse?> RescrapeAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Id == sourceId, ct);
        if (source is null) return null;

        if (WebsiteScrapeStatus.IsInProgress(source.Status)
            || await _db.KnowledgeWebsiteScrapeRuns.AnyAsync(r => r.WebsiteSourceId == sourceId
                && (r.Status == WebsiteScrapeStatus.Pending || r.Status == WebsiteScrapeStatus.Crawling), ct))
        {
            throw new ArgumentException("A crawl of this website is already running.");
        }
        if (!source.IsActive) throw new ArgumentException("Activate this website before re-scraping it.");

        var run = NewRun(source);
        source.Status = WebsiteScrapeStatus.Pending;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        _db.KnowledgeWebsiteScrapeRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        _queue.Enqueue(run.Id);
        return ToResponse(source, run, (await IndexedCountsAsync(clinicId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId));
    }

    public async Task<WebsiteSourceResponse?> SetActiveAsync(Guid clinicId, Guid sourceId, bool isActive, CancellationToken ct = default)
    {
        var source = await _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Id == sourceId, ct);
        if (source is null) return null;

        source.IsActive = isActive;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Documents currently in the KB for this website follow the source. (Deactivating hides all of them;
        // activating restores those of pages that are in the KB — never pages that were removed/failed.)
        var docIds = await _db.KnowledgeWebsitePages
            .Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId && p.KnowledgeDocumentId != null
                        && (!isActive || p.Status == WebsitePageStatus.Indexed || p.Status == WebsitePageStatus.Unchanged))
            .Select(p => p.KnowledgeDocumentId!.Value).ToListAsync(ct);
        foreach (var id in docIds) await _knowledge.SetActiveAsync(clinicId, id, isActive, ct);

        var latest = await _db.KnowledgeWebsiteScrapeRuns.AsNoTracking().Where(r => r.WebsiteSourceId == sourceId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        return ToResponse(source, latest, (await IndexedCountsAsync(clinicId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId));
    }

    public async Task<bool> DeleteAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _db.KnowledgeWebsiteSources.FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Id == sourceId, ct);
        if (source is null) return false;

        if (await _db.KnowledgeWebsiteScrapeRuns.AnyAsync(r => r.WebsiteSourceId == sourceId
                && (r.Status == WebsiteScrapeStatus.Pending || r.Status == WebsiteScrapeStatus.Crawling), ct))
        {
            throw new ArgumentException("A crawl of this website is still running. Wait for it to finish, then delete it.");
        }

        // The knowledge documents this crawl created (and their chunks) go with it — only those, only this clinic.
        var docIds = await _db.KnowledgeWebsitePages
            .Where(p => p.ClinicId == clinicId && p.WebsiteSourceId == sourceId && p.KnowledgeDocumentId != null)
            .Select(p => p.KnowledgeDocumentId!.Value).ToListAsync(ct);
        foreach (var id in docIds) await _knowledge.DeleteAsync(clinicId, id, ct);

        _db.KnowledgeWebsiteSources.Remove(source); // pages + runs cascade
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------
    private static KnowledgeWebsiteScrapeRun NewRun(KnowledgeWebsiteSource source) => new()
    {
        Id = Guid.NewGuid(),
        ClinicId = source.ClinicId,
        WebsiteSourceId = source.Id,
        Status = WebsiteScrapeStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private async Task<Dictionary<Guid, int>> IndexedCountsAsync(Guid clinicId, List<Guid> sourceIds, CancellationToken ct) =>
        await _db.KnowledgeWebsitePages.AsNoTracking()
            .Where(p => p.ClinicId == clinicId && sourceIds.Contains(p.WebsiteSourceId)
                        && (p.Status == WebsitePageStatus.Indexed || p.Status == WebsitePageStatus.Unchanged))
            .GroupBy(p => p.WebsiteSourceId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

    private static WebsiteSourceResponse ToResponse(KnowledgeWebsiteSource s, KnowledgeWebsiteScrapeRun? latest, int indexedPages) => new(
        s.Id, s.StartUrl, s.Host, s.CrawlMode, s.Category, s.IsActive, s.Status, WebsiteScrapeStatus.IsInProgress(s.Status),
        s.LastScrapedAt, s.CreatedAt, indexedPages, latest is null ? null : ToRunResponse(latest));

    private static WebsiteRunResponse ToRunResponse(KnowledgeWebsiteScrapeRun r) => new(
        r.Id, r.Status, r.StartedAt, r.CompletedAt, r.PagesDiscovered, r.PagesProcessed, r.PagesIndexed, r.PagesNew, r.PagesChanged,
        r.PagesUnchanged, r.PagesSkipped, r.PagesDuplicate, r.PagesFailed, r.PagesRemoved, r.ErrorSummary, r.CreatedAt);
}
