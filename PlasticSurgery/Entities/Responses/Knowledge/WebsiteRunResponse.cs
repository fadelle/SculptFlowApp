namespace PlasticSurgery.Entities.Responses.Knowledge;

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
