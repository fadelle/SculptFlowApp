namespace PlasticSurgery.Entities.Responses.Knowledge;

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
    long? FileSizeBytes = null,
    /// <summary>The page URL for a website-imported document (source_type "website"); null otherwise.</summary>
    string? SourceUrl = null
);
