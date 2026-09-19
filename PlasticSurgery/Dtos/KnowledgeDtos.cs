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
    DateTimeOffset UpdatedAt,
    /// <summary>"manual" or "upload" — see KnowledgeSourceType. Upload metadata below is null for manual entries.</summary>
    string SourceType = "manual",
    string? OriginalFileName = null,
    string? MimeType = null,
    long? FileSizeBytes = null
);

/// <summary>What the upload form supplies. The file itself is passed separately as a stream; the clinic
/// is never part of this — it comes from CurrentClinicContext.</summary>
public record UploadKnowledgeRequest(string? Title, string Category, bool IsActive = true);

/// <summary>Body for creating/updating a Knowledge Base document from the dashboard.</summary>
public record SaveKnowledgeRequest(string Title, string Category, string Content, bool IsActive = true);

public record SetKnowledgeActiveRequest(bool IsActive);

/// <summary>Body for POST /api/ai/knowledge/search. ClinicId is the one the n8n workflow already has
/// in context (the AI agent supplies only Query) — nothing else about the conversation is passed.</summary>
public record KnowledgeSearchRequest(Guid ClinicId, string Query, int? Limit = null);

/// <summary>All of a clinic's Knowledge Base retrieval/embedding settings, as persisted. The first four
/// are read-only (see KnowledgeSearchSettings); the rest are the tunable ones.</summary>
public record KnowledgeSettingsResponse(
    string EmbeddingModel,
    int VectorDimension,
    string SimilarityMethod,
    string VectorIndexType,
    int ChunkSizeTokens,
    int ChunkOverlapTokens,
    int TopK,
    double MinimumSimilarity,
    DateTimeOffset UpdatedAt
);

/// <summary>The ONLY settings staff can change. Deliberately has no embedding-model / dimension /
/// similarity / index fields — those can't be changed from the clinic UI or API.</summary>
public record UpdateKnowledgeSettingsRequest(int ChunkSizeTokens, int ChunkOverlapTokens, int TopK, double MinimumSimilarity);

public record KnowledgeSearchResult(Guid DocumentId, string Title, string Category, string Content, double Score);

public record KnowledgeSearchResponse(IReadOnlyList<KnowledgeSearchResult> Results);

/// <summary>multipart/form-data body for POST /api/knowledge/upload — ONE file. No clinicId: the clinic
/// comes from the logged-in user.</summary>
public class KnowledgeUploadForm
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
}
