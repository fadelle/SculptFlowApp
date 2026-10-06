using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

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
