namespace PlasticSurgery.Data.Entities;

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

/// <summary>One crawl of a website source — progress while it runs, history afterwards.</summary>
public class KnowledgeWebsiteScrapeRun
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid WebsiteSourceId { get; set; }

    public string Status { get; set; } = WebsiteScrapeStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int PagesDiscovered { get; set; }
    public int PagesProcessed { get; set; }
    /// <summary>New + changed pages that were (re)indexed.</summary>
    public int PagesIndexed { get; set; }
    public int PagesNew { get; set; }
    public int PagesChanged { get; set; }
    public int PagesUnchanged { get; set; }
    public int PagesSkipped { get; set; }
    public int PagesDuplicate { get; set; }
    public int PagesFailed { get; set; }
    public int PagesRemoved { get; set; }

    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class WebsiteCrawlMode
{
    public const string SinglePage = "single_page";
    public const string CrawlSite = "crawl_site";
    public static bool IsValid(string? v) => v is SinglePage or CrawlSite;
}

/// <summary>Values for a source's status and a run's status — must match schema.sql's CHECKs.</summary>
public static class WebsiteScrapeStatus
{
    public const string Pending = "pending";
    public const string Crawling = "crawling";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completed_with_errors";
    public const string Failed = "failed";

    public static bool IsInProgress(string status) => status is Pending or Crawling;
}

/// <summary>Values for KnowledgeWebsitePage.Status — must match schema.sql's CHECK.</summary>
public static class WebsitePageStatus
{
    public const string Discovered = "discovered";
    public const string Processing = "processing";
    public const string Indexed = "indexed";
    public const string Unchanged = "unchanged";
    public const string Skipped = "skipped";
    public const string Duplicate = "duplicate";
    public const string Failed = "failed";
    public const string Removed = "removed";
}
