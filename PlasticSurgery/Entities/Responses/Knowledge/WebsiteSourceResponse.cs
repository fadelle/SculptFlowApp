namespace PlasticSurgery.Entities.Responses.Knowledge;

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
