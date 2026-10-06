using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>A URL the crawler knows about, and what happened to it. Holds crawl STATE and metadata only —
/// the extracted text is knowledge_documents.content and the vectors are knowledge_chunks.</summary>
public class KnowledgeWebsitePage
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid WebsiteSourceId { get; set; }

    public string Url { get; set; } = string.Empty;
    public string NormalizedUrl { get; set; } = string.Empty;
    public string? CanonicalUrl { get; set; }
    public string? Title { get; set; }

    public int? HttpStatus { get; set; }
    public string? ContentType { get; set; }

    /// <summary>SHA-256 (hex) of the normalized extracted text — never of the raw HTML.</summary>
    public string? ContentHash { get; set; }
    public string? ETag { get; set; }
    public string? LastModifiedHeader { get; set; }

    /// <summary>See <see cref="WebsitePageStatus"/>.</summary>
    public string Status { get; set; } = WebsitePageStatus.Discovered;
    public string? FailureReason { get; set; }
    public int Depth { get; set; }

    public Guid? KnowledgeDocumentId { get; set; }
    public Guid? DuplicateOfPageId { get; set; }

    /// <summary>JSON array of the page's internal links (normalized, capped).</summary>
    public string? Links { get; set; }
    public int MissingCount { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }

    public DateTimeOffset FirstDiscoveredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastScrapedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
