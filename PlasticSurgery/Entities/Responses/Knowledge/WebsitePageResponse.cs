namespace PlasticSurgery.Entities.Responses.Knowledge;

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
