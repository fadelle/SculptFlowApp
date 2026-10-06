using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>
/// One piece of clinic-approved knowledge staff typed into the Knowledge Base (an FAQ, a policy,
/// doctor info, pricing, ...). The AI agent never reads this table directly — content is split into
/// KnowledgeChunk rows (with embeddings) and searched semantically via IKnowledgeSearchService.
/// Everything here is scoped by ClinicId; knowledge from one clinic is never visible to another.
/// </summary>
public class KnowledgeDocument
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Free string (see <see cref="KnowledgeCategory"/> for the values the UI offers) —
    /// deliberately no DB CHECK, like campaigns.campaign_type, so new categories need no migration.</summary>
    public string Category { get; set; } = KnowledgeCategory.General;

    /// <summary>The text that gets chunked and embedded. For a manual entry it's what staff typed; for an
    /// uploaded file it's the normalized text extracted from it — so re-indexing never needs the original file.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>See <see cref="KnowledgeSourceType"/>.</summary>
    public string SourceType { get; set; } = KnowledgeSourceType.Manual;

    // Upload metadata (null for manual entries). The original binary is deliberately NOT retained.
    public string? OriginalFileName { get; set; }
    public string? MimeType { get; set; }
    public long? FileSizeBytes { get; set; }

    /// <summary>The page URL a website-sourced document came from (null otherwise). The document is
    /// created/updated by the website-scraping subsystem; its text still lives in <see cref="Content"/>.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Inactive documents keep their chunks stored but are excluded from AI search.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}
