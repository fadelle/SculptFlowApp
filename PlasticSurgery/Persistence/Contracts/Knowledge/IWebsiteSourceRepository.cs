using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Knowledge;

/// <summary>Knowledge Base website sources, their scrape runs and crawled pages. Tracked unless "ReadOnly".</summary>
public interface IWebsiteSourceRepository
{
    // ---- sources
    void AddSource(KnowledgeWebsiteSource source);

    void RemoveSource(KnowledgeWebsiteSource source);

    Task<int> CountSourcesAsync(Guid clinicId, CancellationToken ct = default);

    Task<bool> SourceUrlExistsAsync(Guid clinicId, string normalizedStartUrl, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<KnowledgeWebsiteSource>> ListSourcesReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    Task<KnowledgeWebsiteSource?> GetSourceReadOnlyAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);

    Task<KnowledgeWebsiteSource?> GetSourceAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);

    /// <summary>By id alone (background crawls, recovery).</summary>
    Task<KnowledgeWebsiteSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken ct = default);

    /// <summary>Re-reads the source from the database (picks up changes made by another request meanwhile).</summary>
    Task ReloadAsync(KnowledgeWebsiteSource source, CancellationToken ct = default);

    // ---- runs
    void AddRun(KnowledgeWebsiteScrapeRun run);

    Task<KnowledgeWebsiteScrapeRun?> GetRunAsync(Guid runId, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeWebsiteScrapeRun>> ListRunsReadOnlyAsync(Guid clinicId, IReadOnlyCollection<Guid> sourceIds,
        CancellationToken ct = default);

    /// <summary>The source's most recent runs, newest first.</summary>
    Task<IReadOnlyList<KnowledgeWebsiteScrapeRun>> ListRecentRunsReadOnlyAsync(Guid clinicId, Guid sourceId, int take,
        CancellationToken ct = default);

    Task<KnowledgeWebsiteScrapeRun?> GetLatestRunReadOnlyAsync(Guid sourceId, CancellationToken ct = default);

    /// <summary>Is a pending or crawling run open for the source?</summary>
    Task<bool> HasRunInProgressAsync(Guid sourceId, CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeWebsiteScrapeRun>> ListRunsInStatusAsync(string status, CancellationToken ct = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListRunIdsInStatusAsync(string status, CancellationToken ct = default);

    // ---- pages
    void AddPage(KnowledgeWebsitePage page);

    /// <summary>Every page of the source, keyed by normalized URL (ordinal).</summary>
    Task<Dictionary<string, KnowledgeWebsitePage>> MapPagesByUrlAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Read-only, by depth then URL.</summary>
    Task<IReadOnlyList<KnowledgeWebsitePage>> ListPagesReadOnlyAsync(Guid clinicId, Guid sourceId, string? status, int skip, int take,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, int>> CountPagesByStatusAsync(Guid clinicId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Pages currently in the Knowledge Base (indexed or unchanged), per source.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountIndexedPagesAsync(Guid clinicId, IReadOnlyCollection<Guid> sourceIds,
        CancellationToken ct = default);

    /// <summary>Knowledge documents created from the source's pages; with <paramref name="onlyInKnowledgeBase"/>, only
    /// pages currently indexed or unchanged.</summary>
    Task<IReadOnlyList<Guid>> ListPageDocumentIdsAsync(Guid clinicId, Guid sourceId, bool onlyInKnowledgeBase,
        CancellationToken ct = default);
}
