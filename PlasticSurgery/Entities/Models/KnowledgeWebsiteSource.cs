using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>
/// One website configured as a Knowledge Base source for a clinic. Owned by the standalone website-scraping
/// subsystem (Integrations/Knowledge/WebScraping); the pages it discovers become ordinary
/// knowledge_documents (source_type = "website") via <see cref="KnowledgeWebsitePage.KnowledgeDocumentId"/>.
/// </summary>
public class KnowledgeWebsiteSource
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    /// <summary>What staff typed.</summary>
    public string StartUrl { get; set; } = string.Empty;
    /// <summary>Identity of the source (unique per clinic) — see UrlNormalizer.</summary>
    public string NormalizedStartUrl { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;

    /// <summary>See <see cref="WebsiteCrawlMode"/>.</summary>
    public string CrawlMode { get; set; } = WebsiteCrawlMode.CrawlSite;
    /// <summary>Knowledge Base category given to every page document from this website.</summary>
    public string Category { get; set; } = KnowledgeCategory.General;

    /// <summary>Inactive sources keep their pages/documents but the documents are excluded from AI search.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Status of the most recent run — see <see cref="WebsiteScrapeStatus"/>.</summary>
    public string Status { get; set; } = WebsiteScrapeStatus.Pending;
    public DateTimeOffset? LastScrapedAt { get; set; }

    /// <summary>JSON array of SHA-256 hashes of text blocks repeated across the site (menus, footers).</summary>
    public string? BoilerplateBlockHashes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
