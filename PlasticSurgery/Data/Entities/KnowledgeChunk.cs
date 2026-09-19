namespace PlasticSurgery.Data.Entities;

/// <summary>
/// One embedded slice of a KnowledgeDocument's content. The pgvector `embedding` column is
/// deliberately NOT mapped on this entity: chunk rows are written and searched with raw SQL
/// (see KnowledgeService / KnowledgeSearchService), which keeps the project free of an EF-vector
/// package dependency. Never insert these through DbContext.Add — the embedding column is NOT NULL.
/// ClinicId is denormalized from the parent document so every search filters by clinic directly.
/// </summary>
public class KnowledgeChunk
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid KnowledgeDocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public KnowledgeDocument? Document { get; set; }
}
