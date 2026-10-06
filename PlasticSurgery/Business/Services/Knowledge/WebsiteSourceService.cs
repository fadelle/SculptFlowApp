using PlasticSurgery.Business.Contracts.Jobs;
using PlasticSurgery.Business.Contracts.Services.Knowledge;
using PlasticSurgery.Business.Engines.WebScraping;
using PlasticSurgery.Common.Configs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Knowledge;
using PlasticSurgery.Entities.Responses.Knowledge;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Business.Services.Knowledge;

public sealed class WebsiteSourceService : IWebsiteSourceService
{
    private readonly IWebsiteSourceRepository _websites;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteScrapeQueue _queue;
    private readonly SsrfGuard _guard;
    private readonly WebsiteScrapeOptions _options;

    public WebsiteSourceService(
        IWebsiteSourceRepository websites, IUnitOfWork unitOfWork, IKnowledgeService knowledge, IWebsiteScrapeQueue queue, SsrfGuard guard, WebsiteScrapeOptions options)
    {
        _websites = websites;
        _unitOfWork = unitOfWork;
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

        if (await _websites.CountSourcesAsync(clinicId, ct) >= _options.MaxSourcesPerClinic)
        {
            throw new ArgumentException($"You can add up to {_options.MaxSourcesPerClinic} websites. Delete one you no longer need first.");
        }
        if (await _websites.SourceUrlExistsAsync(clinicId, normalized, ct))
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
        _websites.AddSource(source);
        _websites.AddRun(run);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateRecordException)
        {
            throw new ArgumentException("This website address has already been added.");
        }

        _queue.Enqueue(run.Id); // after the commit, so the worker can always find the run
        return ToResponse(source, run, 0);
    }

    public async Task<IReadOnlyList<WebsiteSourceResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var sources = await _websites.ListSourcesReadOnlyAsync(clinicId, ct);
        var ids = sources.Select(s => s.Id).ToList();

        var runs = await _websites.ListRunsReadOnlyAsync(clinicId, ids, ct);
        var latest = runs.GroupBy(r => r.WebsiteSourceId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());

        var indexed = await IndexedCountsAsync(clinicId, ids, ct);
        return sources.Select(s => ToResponse(s, latest.GetValueOrDefault(s.Id), indexed.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<WebsiteSourceDetailResponse?> GetAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceReadOnlyAsync(clinicId, sourceId, ct);
        if (source is null) return null;

        var runs = await _websites.ListRecentRunsReadOnlyAsync(clinicId, sourceId, 8, ct);

        var counts = (await _websites.CountPagesByStatusAsync(clinicId, sourceId, ct)).ToDictionary(x => x.Key, x => x.Value);

        var indexed = (await IndexedCountsAsync(clinicId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId);
        return new WebsiteSourceDetailResponse(
            ToResponse(source, runs.FirstOrDefault(), indexed), runs.Select(ToRunResponse).ToList(), counts);
    }

    public async Task<IReadOnlyList<WebsitePageResponse>> ListPagesAsync(
        Guid clinicId, Guid sourceId, string? status, int skip, int take, CancellationToken ct = default)
    {
        return (await _websites.ListPagesReadOnlyAsync(clinicId, sourceId, status, Math.Max(0, skip), Math.Clamp(take, 1, 500), ct))
            .Select(p => new WebsitePageResponse(p.Id, p.NormalizedUrl, p.Title, p.Status, p.HttpStatus, p.FailureReason,
                p.CanonicalUrl, p.KnowledgeDocumentId, p.Depth, p.LastScrapedAt))
            .ToList();
    }

    public async Task<WebsiteSourceResponse?> RescrapeAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceAsync(clinicId, sourceId, ct);
        if (source is null) return null;

        if (WebsiteScrapeStatus.IsInProgress(source.Status)
            || await _websites.HasRunInProgressAsync(sourceId, ct))
        {
            throw new ArgumentException("A crawl of this website is already running.");
        }
        if (!source.IsActive) throw new ArgumentException("Activate this website before re-scraping it.");

        var run = NewRun(source);
        source.Status = WebsiteScrapeStatus.Pending;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        _websites.AddRun(run);
        await _unitOfWork.SaveChangesAsync(ct);

        _queue.Enqueue(run.Id);
        return ToResponse(source, run, (await IndexedCountsAsync(clinicId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId));
    }

    public async Task<WebsiteSourceResponse?> SetActiveAsync(Guid clinicId, Guid sourceId, bool isActive, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceAsync(clinicId, sourceId, ct);
        if (source is null) return null;

        source.IsActive = isActive;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        // Documents currently in the KB for this website follow the source. (Deactivating hides all of them;
        // activating restores those of pages that are in the KB — never pages that were removed/failed.)
        var docIds = await _websites.ListPageDocumentIdsAsync(clinicId, sourceId, onlyInKnowledgeBase: isActive, ct);
        foreach (var id in docIds) await _knowledge.SetActiveAsync(clinicId, id, isActive, ct);

        var latest = await _websites.GetLatestRunReadOnlyAsync(sourceId, ct);
        return ToResponse(source, latest, (await IndexedCountsAsync(clinicId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId));
    }

    public async Task<bool> DeleteAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceAsync(clinicId, sourceId, ct);
        if (source is null) return false;

        if (await _websites.HasRunInProgressAsync(sourceId, ct))
        {
            throw new ArgumentException("A crawl of this website is still running. Wait for it to finish, then delete it.");
        }

        // The knowledge documents this crawl created (and their chunks) go with it — only those, only this clinic.
        var docIds = await _websites.ListPageDocumentIdsAsync(clinicId, sourceId, onlyInKnowledgeBase: false, ct);
        foreach (var id in docIds) await _knowledge.DeleteAsync(clinicId, id, ct);

        _websites.RemoveSource(source); // pages + runs cascade
        await _unitOfWork.SaveChangesAsync(ct);
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

    private Task<IReadOnlyDictionary<Guid, int>> IndexedCountsAsync(Guid clinicId, List<Guid> sourceIds, CancellationToken ct) =>
        _websites.CountIndexedPagesAsync(clinicId, sourceIds, ct);

    private static WebsiteSourceResponse ToResponse(KnowledgeWebsiteSource s, KnowledgeWebsiteScrapeRun? latest, int indexedPages) => new(
        s.Id, s.StartUrl, s.Host, s.CrawlMode, s.Category, s.IsActive, s.Status, WebsiteScrapeStatus.IsInProgress(s.Status),
        s.LastScrapedAt, s.CreatedAt, indexedPages, latest is null ? null : ToRunResponse(latest));

    private static WebsiteRunResponse ToRunResponse(KnowledgeWebsiteScrapeRun r) => new(
        r.Id, r.Status, r.StartedAt, r.CompletedAt, r.PagesDiscovered, r.PagesProcessed, r.PagesIndexed, r.PagesNew, r.PagesChanged,
        r.PagesUnchanged, r.PagesSkipped, r.PagesDuplicate, r.PagesFailed, r.PagesRemoved, r.ErrorSummary, r.CreatedAt);
}
