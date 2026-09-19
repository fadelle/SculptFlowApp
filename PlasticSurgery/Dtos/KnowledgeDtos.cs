namespace PlasticSurgery.Dtos;

public record KnowledgeDocumentResponse(
    Guid Id,
    Guid ClinicId,
    string Title,
    string Category,
    string Content,
    bool IsActive,
    int ChunkCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>Body for creating/updating a Knowledge Base document from the dashboard.</summary>
public record SaveKnowledgeRequest(string Title, string Category, string Content, bool IsActive = true);

public record SetKnowledgeActiveRequest(bool IsActive);

/// <summary>Body for POST /api/ai/knowledge/search. ClinicId is the one the n8n workflow already has
/// in context (the AI agent supplies only Query) — nothing else about the conversation is passed.</summary>
public record KnowledgeSearchRequest(Guid ClinicId, string Query, int? Limit = null);

public record KnowledgeSearchResult(Guid DocumentId, string Title, string Category, string Content, double Score);

public record KnowledgeSearchResponse(IReadOnlyList<KnowledgeSearchResult> Results);
