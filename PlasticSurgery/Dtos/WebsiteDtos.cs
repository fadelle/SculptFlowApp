namespace PlasticSurgery.Dtos;

/// <summary>Body for POST /api/knowledge/websites. The clinic comes from the logged-in user, never from here.</summary>
public record CreateWebsiteSourceRequest(string? Url, string? CrawlMode, string? Category, bool IsActive = true);

/// <summary>Counts of the most recent crawl. All zero until it has run.</summary>
public record WebsiteRunResponse(
    Guid Id,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int PagesDiscovered,
    int PagesProcessed,
    int PagesIndexed,
    int PagesNew,
    int PagesChanged,
    int PagesUnchanged,
    int PagesSkipped,
    int PagesDuplicate,
    int PagesFailed,
    int PagesRemoved,
    string? ErrorSummary,
    DateTimeOffset CreatedAt
);

public record WebsiteSourceResponse(
    Guid Id,
    string StartUrl,
    string Host,
    string CrawlMode,
    string Category,
    bool IsActive,
    string Status,
    bool InProgress,
    DateTimeOffset? LastScrapedAt,
    DateTimeOffset CreatedAt,
    /// <summary>Pages currently in the Knowledge Base (indexed or unchanged).</summary>
    int IndexedPages,
    WebsiteRunResponse? LatestRun
);

public record WebsitePageResponse(
    Guid Id,
    string Url,
    string? Title,
    string Status,
    int? HttpStatus,
    string? FailureReason,
    string? CanonicalUrl,
    Guid? KnowledgeDocumentId,
    int Depth,
    DateTimeOffset? LastScrapedAt
);

public record WebsiteSourceDetailResponse(
    WebsiteSourceResponse Source,
    IReadOnlyList<WebsiteRunResponse> RecentRuns,
    IReadOnlyDictionary<string, int> PageStatusCounts
);
