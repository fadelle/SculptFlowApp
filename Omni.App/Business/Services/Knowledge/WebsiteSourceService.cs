using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Engines.WebScraping;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Knowledge;

namespace Omni.App.Business.Services.Knowledge;

public sealed class WebsiteSourceService : IWebsiteSourceService
{
    private readonly IWebsiteSourceRepository _websites;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteScrapeQueue _queue;
    private readonly SsrfGuard _guard;
    private readonly IConfigManager _config;

    public WebsiteSourceService(
        IWebsiteSourceRepository websites, IUnitOfWork unitOfWork, IKnowledgeService knowledge, IWebsiteScrapeQueue queue, SsrfGuard guard, IConfigManager config)
    {
        _websites = websites;
        _unitOfWork = unitOfWork;
        _knowledge = knowledge;
        _queue = queue;
        _guard = guard;
        _config = config;
    }

    public async Task<WebsiteSourceResponse> CreateAsync(Guid clientId, CreateWebsiteSourceRequest request, CancellationToken ct = default)
    {
        var raw = (request.Url ?? string.Empty).Trim();
        if (raw.Length == 0) throw new ArgumentException("knowledge.websites.errors.urlRequired");
        // Be forgiving: "client.com" → https://client.com
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;

        if (!UrlNormalizer.TryNormalize(raw, null, out var normalized))
        {
            throw new ArgumentException("knowledge.websites.errors.urlInvalid");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("knowledge.websites.errors.urlInvalid");
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
        if (!WebsiteCrawlMode.IsValid(mode)) throw new ArgumentException("knowledge.websites.errors.modeInvalid");

        var category = string.IsNullOrWhiteSpace(request.Category) ? KnowledgeCategory.General : request.Category.Trim().ToLowerInvariant();
        if (category.Length > 50) throw new ArgumentException("knowledge.websites.errors.categoryTooLong");

        if (await _websites.CountSourcesAsync(clientId, ct) >= _config.WebScrapingMaxSourcesPerClient)
        {
            throw new ArgumentException("knowledge.websites.errors.tooMany").WithArgs(_config.WebScrapingMaxSourcesPerClient);
        }
        if (await _websites.SourceUrlExistsAsync(clientId, normalized, ct))
        {
            throw new ArgumentException("knowledge.websites.errors.alreadyAddedRescrape");
        }

        var now = DateTimeOffset.UtcNow;
        var source = new KnowledgeWebsiteSource
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
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
            throw new ArgumentException("knowledge.websites.errors.alreadyAdded");
        }

        _queue.Enqueue(run.Id); // after the commit, so the worker can always find the run
        return ToResponse(source, run, 0);
    }

    public async Task<IReadOnlyList<WebsiteSourceResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var sources = await _websites.ListSourcesReadOnlyAsync(clientId, ct);
        var ids = sources.Select(s => s.Id).ToList();

        var runs = await _websites.ListRunsReadOnlyAsync(clientId, ids, ct);
        var latest = runs.GroupBy(r => r.WebsiteSourceId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());

        var indexed = await IndexedCountsAsync(clientId, ids, ct);
        return sources.Select(s => ToResponse(s, latest.GetValueOrDefault(s.Id), indexed.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<WebsiteSourceDetailResponse?> GetAsync(Guid clientId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceReadOnlyAsync(clientId, sourceId, ct);
        if (source is null) return null;

        var runs = await _websites.ListRecentRunsReadOnlyAsync(clientId, sourceId, 8, ct);

        var counts = (await _websites.CountPagesByStatusAsync(clientId, sourceId, ct)).ToDictionary(x => x.Key, x => x.Value);

        var indexed = (await IndexedCountsAsync(clientId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId);
        return new WebsiteSourceDetailResponse(
            ToResponse(source, runs.FirstOrDefault(), indexed), runs.Select(ToRunResponse).ToList(), counts);
    }

    public async Task<IReadOnlyList<WebsitePageResponse>> ListPagesAsync(
        Guid clientId, Guid sourceId, string? status, int skip, int take, CancellationToken ct = default)
    {
        return (await _websites.ListPagesReadOnlyAsync(clientId, sourceId, status, Math.Max(0, skip), Math.Clamp(take, 1, 500), ct))
            .Select(p => new WebsitePageResponse(p.Id, p.NormalizedUrl, p.Title, p.Status, p.HttpStatus, p.FailureReason,
                p.CanonicalUrl, p.KnowledgeDocumentId, p.Depth, p.LastScrapedAt))
            .ToList();
    }

    public async Task<WebsiteSourceResponse?> RescrapeAsync(Guid clientId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceAsync(clientId, sourceId, ct);
        if (source is null) return null;

        if (WebsiteScrapeStatus.IsInProgress(source.Status)
            || await _websites.HasRunInProgressAsync(sourceId, ct))
        {
            throw new ArgumentException("knowledge.websites.errors.crawlRunning");
        }
        if (!source.IsActive) throw new ArgumentException("knowledge.websites.errors.activateFirst");

        var run = NewRun(source);
        source.Status = WebsiteScrapeStatus.Pending;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        _websites.AddRun(run);
        await _unitOfWork.SaveChangesAsync(ct);

        _queue.Enqueue(run.Id);
        return ToResponse(source, run, (await IndexedCountsAsync(clientId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId));
    }

    public async Task<WebsiteSourceResponse?> SetActiveAsync(Guid clientId, Guid sourceId, bool isActive, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceAsync(clientId, sourceId, ct);
        if (source is null) return null;

        source.IsActive = isActive;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        // Documents currently in the KB for this website follow the source. (Deactivating hides all of them;
        // activating restores those of pages that are in the KB — never pages that were removed/failed.)
        var docIds = await _websites.ListPageDocumentIdsAsync(clientId, sourceId, onlyInKnowledgeBase: isActive, ct);
        foreach (var id in docIds) await _knowledge.SetActiveAsync(clientId, id, isActive, ct);

        var latest = await _websites.GetLatestRunReadOnlyAsync(sourceId, ct);
        return ToResponse(source, latest, (await IndexedCountsAsync(clientId, new List<Guid> { sourceId }, ct)).GetValueOrDefault(sourceId));
    }

    public async Task<bool> DeleteAsync(Guid clientId, Guid sourceId, CancellationToken ct = default)
    {
        var source = await _websites.GetSourceAsync(clientId, sourceId, ct);
        if (source is null) return false;

        if (await _websites.HasRunInProgressAsync(sourceId, ct))
        {
            throw new ArgumentException("knowledge.websites.errors.crawlRunningDelete");
        }

        // The knowledge documents this crawl created (and their chunks) go with it — only those, only this client.
        var docIds = await _websites.ListPageDocumentIdsAsync(clientId, sourceId, onlyInKnowledgeBase: false, ct);
        foreach (var id in docIds) await _knowledge.DeleteAsync(clientId, id, ct);

        _websites.RemoveSource(source); // pages + runs cascade
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    // ---------------------------------------------------------------------------------------------------
    private static KnowledgeWebsiteScrapeRun NewRun(KnowledgeWebsiteSource source) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = source.ClientId,
        WebsiteSourceId = source.Id,
        Status = WebsiteScrapeStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private Task<IReadOnlyDictionary<Guid, int>> IndexedCountsAsync(Guid clientId, List<Guid> sourceIds, CancellationToken ct) =>
        _websites.CountIndexedPagesAsync(clientId, sourceIds, ct);

    private static WebsiteSourceResponse ToResponse(KnowledgeWebsiteSource s, KnowledgeWebsiteScrapeRun? latest, int indexedPages) => new(
        s.Id, s.StartUrl, s.Host, s.CrawlMode, s.Category, s.IsActive, s.Status, WebsiteScrapeStatus.IsInProgress(s.Status),
        s.LastScrapedAt, s.CreatedAt, indexedPages, latest is null ? null : ToRunResponse(latest));

    private static WebsiteRunResponse ToRunResponse(KnowledgeWebsiteScrapeRun r) => new(
        r.Id, r.Status, r.StartedAt, r.CompletedAt, r.PagesDiscovered, r.PagesProcessed, r.PagesIndexed, r.PagesNew, r.PagesChanged,
        r.PagesUnchanged, r.PagesSkipped, r.PagesDuplicate, r.PagesFailed, r.PagesRemoved, r.ErrorSummary, r.CreatedAt);
}
