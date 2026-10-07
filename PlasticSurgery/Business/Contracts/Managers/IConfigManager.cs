using PlasticSurgery.Entities.Dtos.Configuration;

namespace PlasticSurgery.Business.Contracts.Managers;

/// <summary>
/// Reads settings by section and key: the value stored in config.settings when there is one, otherwise the setting's
/// constant default from Common/Statics/ConfigDefaults. Use the typed properties (one per declared setting); the
/// Get methods are for code that works on settings generically. Values come from an in-memory copy of the table,
/// refreshed every minute and right after a change, so reads never wait on the database and changes apply at once.
/// Asking for a setting that ConfigDefaults doesn't declare throws (a programming error, not a missing value).
/// </summary>
public interface IConfigManager
{
    // ---- Availability
    /// <summary>Consultation length (minutes) for clinics that haven't set their own booking settings. Default 30.</summary>
    int AvailabilityDefaultDurationMinutes { get; }

    /// <summary>Gap (minutes) between consultations for clinics that haven't set their own booking settings. Default 0.</summary>
    int AvailabilityDefaultBufferMinutes { get; }

    /// <summary>Minimum notice (minutes) before a bookable slot, for clinics that haven't set their own. Default 240.</summary>
    int AvailabilityDefaultNoticeMinutes { get; }

    /// <summary>How many days ahead patients can book, for clinics that haven't set their own. Default 60.</summary>
    int AvailabilityDefaultHorizonDays { get; }

    /// <summary>The most days one free-slots request may cover. Default 14.</summary>
    int AvailabilityMaxRangeDays { get; }

    // ---- Billing
    /// <summary>Master switch for usage billing and plan entitlements. Off = nothing is rated or reserved and every clinic may use everything. Default true.</summary>
    bool BillingEnabled { get; }

    /// <summary>How long a past-due subscription keeps working before it expires. Default 7.</summary>
    int BillingGracePeriodDays { get; }

    /// <summary>A message charge still waiting for its delivery outcome is settled or released after this long. Default 72.</summary>
    int BillingReservationTimeoutHours { get; }

    /// <summary>How often the billing worker renews due subscriptions and resolves stale reservations. Default 5.</summary>
    int BillingMaintenanceIntervalMinutes { get; }

    /// <summary>Plan code a newly registered clinic starts on (first period free). Empty = no plan until an admin assigns one. Default (empty).</summary>
    string BillingSignupPlanCode { get; }

    /// <summary>The one account currency (ISO 4217, e.g. USD). Balances, plans and rates are stored in it: change it only before any money has moved. Default USD.</summary>
    string BillingCurrency { get; }

    /// <summary>How far in the past a new rate may start or an old one may end (clock skew allowance). Default 5.</summary>
    int BillingRateBackdateToleranceMinutes { get; }

    /// <summary>How many recent wallet transactions a clinic's billing page shows. Default 15.</summary>
    int BillingRecentTransactions { get; }

    // ---- Knowledge
    /// <summary>Minimum similarity for a search result, for new clinics (each clinic can change its own). Default 0.30.</summary>
    decimal KnowledgeMinScore { get; }

    /// <summary>Chunk size in characters for new clinics (stored per clinic in tokens). Default 1000.</summary>
    int KnowledgeChunkMaxChars { get; }

    /// <summary>Chunk overlap in characters for new clinics. Default 150.</summary>
    int KnowledgeChunkOverlapChars { get; }

    /// <summary>A trailing piece shorter than this is merged into the previous chunk. Default 200.</summary>
    int KnowledgeChunkMinChars { get; }

    /// <summary>Largest knowledge-base file a clinic can upload (bytes). Default 5242880.</summary>
    int KnowledgeMaxUploadBytes { get; }

    /// <summary>Most text kept from one uploaded document. Default 250000.</summary>
    int KnowledgeMaxExtractedChars { get; }

    /// <summary>Smallest chunk size a clinic may choose (tokens). Default 50.</summary>
    int KnowledgeMinChunkSizeTokens { get; }

    /// <summary>Largest chunk size a clinic may choose (tokens). Default 1000.</summary>
    int KnowledgeMaxChunkSizeTokens { get; }

    /// <summary>Smallest number of search results a clinic may choose. Default 1.</summary>
    int KnowledgeMinTopK { get; }

    /// <summary>Largest number of search results a clinic may choose. Default 10.</summary>
    int KnowledgeMaxTopK { get; }

    // ---- WebScraping
    /// <summary>Most distinct pages one website crawl visits. Default 100.</summary>
    int WebScrapingMaxPages { get; }

    /// <summary>Link depth from the start page. Default 3.</summary>
    int WebScrapingMaxDepth { get; }

    /// <summary>Simultaneous requests to one website. Default 3.</summary>
    int WebScrapingMaxConcurrency { get; }

    /// <summary>Timeout of one page request. Default 15.</summary>
    int WebScrapingRequestTimeoutSeconds { get; }

    /// <summary>Largest page the crawler reads (bytes, after decompression). Default 2000000.</summary>
    int WebScrapingMaxResponseBytes { get; }

    /// <summary>Redirects followed for one page. Default 5.</summary>
    int WebScrapingMaxRedirects { get; }

    /// <summary>Pause before each request (robots.txt Crawl-delay can raise it). Default 300.</summary>
    int WebScrapingPolitenessDelayMs { get; }

    /// <summary>Wall-clock limit of one crawl. Default 20.</summary>
    int WebScrapingMaxRunMinutes { get; }

    /// <summary>Pages with less readable text are skipped. Default 80.</summary>
    int WebScrapingMinTextChars { get; }

    /// <summary>Most text kept from one page. Default 200000.</summary>
    int WebScrapingMaxTextChars { get; }

    /// <summary>Different query strings allowed for one path (stops calendar and filter traps). Default 5.</summary>
    int WebScrapingMaxQueryVariantsPerPath { get; }

    /// <summary>Most links taken from one page. Default 300.</summary>
    int WebScrapingMaxLinksPerPage { get; }

    /// <summary>How many websites one clinic may add. Default 25.</summary>
    int WebScrapingMaxSourcesPerClinic { get; }

    /// <summary>Development environment only: comma-separated host:port entries the crawler may reach despite the SSRF rules (e.g. a local test site). Ignored outside Development. Default (empty).</summary>
    string WebScrapingDevAllowedHosts { get; }

    /// <summary>The crawler's User-Agent header. Default SculptFlowBot/1.0 (+https://sculptflowapp.onrender.com; clinic knowledge-base crawler).</summary>
    string WebScrapingUserAgent { get; }

    // ---- Benchmark
    /// <summary>Chunks sent to n8n per question-generation request. Default 20.</summary>
    int BenchmarkGenerationSampleSize { get; }

    /// <summary>Chunks sampled to choose the generation chunks from. Default 60.</summary>
    int BenchmarkSamplePoolSize { get; }

    /// <summary>Shortest chunk that may be used to generate a question. Default 80.</summary>
    int BenchmarkMinSourceChunkChars { get; }

    /// <summary>A benchmark run stops after this many retrieval errors in a row. Default 5.</summary>
    int BenchmarkMaxConsecutiveErrors { get; }

    /// <summary>A run still marked running after this long is treated as abandoned. Default 2.</summary>
    int BenchmarkStaleRunHours { get; }

    /// <summary>How long to wait for n8n to send generated questions. Default 30.</summary>
    int BenchmarkGenerationTimeoutMinutes { get; }

    /// <summary>Most generated questions accepted per chunk. Default 3.</summary>
    int BenchmarkMaxQuestionsPerChunk { get; }

    // ---- Campaigns
    /// <summary>Default "inactive for" days of a reactivation audience. Default 60.</summary>
    int CampaignsDefaultInactiveDays { get; }

    // ---- Embeddings
    /// <summary>Base URL of the OpenAI-compatible embeddings API. Default https://api.openai.com/v1.</summary>
    string EmbeddingsBaseUrl { get; }

    /// <summary>Embedding model. Stored vectors are tied to it: changing it means re-embedding every knowledge document. Default text-embedding-3-small.</summary>
    string EmbeddingsModel { get; }

    /// <summary>Vector size the model returns. Must equal the vector(N) size of knowledge.knowledge_chunks.embedding (1536). Default 1536.</summary>
    int EmbeddingsDimensions { get; }

    /// <summary>SECRET. API key of the embeddings API. Never shown back: the Configuration page and API only say whether it is set. Default (empty).</summary>
    string EmbeddingsApiKey { get; }

    /// <summary>Texts sent to the embeddings API in one request. Default 64.</summary>
    int EmbeddingsMaxInputsPerRequest { get; }

    // ---- Integrations
    /// <summary>How long a calendar or TikTok connect link stays valid. Default 15.</summary>
    int IntegrationsOAuthStateLifetimeMinutes { get; }

    /// <summary>Calendar and TikTok access tokens are refreshed this long before they expire. Default 5.</summary>
    int IntegrationsTokenRefreshMarginMinutes { get; }

    // ---- Infobip
    /// <summary>Timeout of one call to Infobip. Default 20.</summary>
    int InfobipTimeoutSeconds { get; }

    /// <summary>Total tries for a send Infobip clearly refused (429/503/no connection). Default 3.</summary>
    int InfobipMaxSendAttempts { get; }

    // ---- Dashboard
    /// <summary>Leads per page on the dashboard. Default 25.</summary>
    int DashboardLeadsPageSize { get; }

    // ---- generic access

    string GetString(string section, string key);

    int GetInt(string section, string key);

    decimal GetDecimal(string section, string key);

    bool GetBool(string section, string key);

    string GetString(ConfigDefinition setting);

    int GetInt(ConfigDefinition setting);

    decimal GetDecimal(ConfigDefinition setting);

    bool GetBool(ConfigDefinition setting);

    /// <summary>Re-reads config.settings now (after a change, and on a timer). Keeps the last values if the database fails.</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
