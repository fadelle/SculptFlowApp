using Omni.App.Entities.Dtos.Configuration;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Reads settings by section and key: the value stored in config.settings when there is one, otherwise the setting's
/// constant default from Common/Statics/ConfigDefaults. Use the typed properties (one per declared setting); the
/// Get methods are for code that works on settings generically. Values come from an in-memory copy of the table,
/// refreshed every minute and right after a change, so reads never wait on the database and changes apply at once.
/// Asking for a setting that ConfigDefaults doesn't declare throws (a programming error, not a missing value).
/// </summary>
public interface IConfigManager
{
    // ---- Accounts
    /// <summary>Whether anyone can create a new client (Register page and "Continue with Google"). Default true.</summary>
    bool AccountsSignupEnabled { get; }

    /// <summary>Most sign-up attempts one IP address may make per hour. Default 20.</summary>
    int AccountsSignupsPerIpPerHour { get; }

    // ---- Availability
    /// <summary>Consultation length (minutes) for clients that haven't set their own booking settings. Default 30.</summary>
    int AvailabilityDefaultDurationMinutes { get; }

    /// <summary>Gap (minutes) between consultations for clients that haven't set their own booking settings. Default 0.</summary>
    int AvailabilityDefaultBufferMinutes { get; }

    /// <summary>Minimum notice (minutes) before a bookable slot, for clients that haven't set their own. Default 240.</summary>
    int AvailabilityDefaultNoticeMinutes { get; }

    /// <summary>How many days ahead patients can book, for clients that haven't set their own. Default 60.</summary>
    int AvailabilityDefaultHorizonDays { get; }

    /// <summary>The most days one free-slots request may cover. Default 14.</summary>
    int AvailabilityMaxRangeDays { get; }

    // ---- Billing
    /// <summary>Master switch for usage billing and plan entitlements. Off = nothing is rated or reserved and every client may use everything. Default true.</summary>
    bool BillingEnabled { get; }

    /// <summary>How long a past-due subscription keeps working before it expires. Default 7.</summary>
    int BillingGracePeriodDays { get; }

    /// <summary>A message charge still waiting for its delivery outcome is settled or released after this long. Default 72.</summary>
    int BillingReservationTimeoutHours { get; }

    /// <summary>How often the billing worker renews due subscriptions and resolves stale reservations. Default 5.</summary>
    int BillingMaintenanceIntervalMinutes { get; }

    /// <summary>Plan code a newly registered client starts on (first period free). Empty = no plan until an admin assigns one. Default (empty).</summary>
    string BillingSignupPlanCode { get; }

    /// <summary>The one account currency (ISO 4217, e.g. USD). Balances, plans and rates are stored in it: change it only before any money has moved. Default USD.</summary>
    string BillingCurrency { get; }

    /// <summary>How far in the past a new rate may start or an old one may end (clock skew allowance). Default 5.</summary>
    int BillingRateBackdateToleranceMinutes { get; }

    /// <summary>How many recent wallet transactions a client's billing page shows. Default 15.</summary>
    int BillingRecentTransactions { get; }

    // ---- Knowledge
    /// <summary>Minimum similarity for a search result, for new clients (each client can change its own). Default 0.30.</summary>
    decimal KnowledgeMinScore { get; }

    /// <summary>Chunk size in characters for new clients (stored per client in tokens). Default 1000.</summary>
    int KnowledgeChunkMaxChars { get; }

    /// <summary>Chunk overlap in characters for new clients. Default 150.</summary>
    int KnowledgeChunkOverlapChars { get; }

    /// <summary>A trailing piece shorter than this is merged into the previous chunk. Default 200.</summary>
    int KnowledgeChunkMinChars { get; }

    /// <summary>Largest knowledge-base file a client can upload (bytes). Default 5242880.</summary>
    int KnowledgeMaxUploadBytes { get; }

    /// <summary>Most text kept from one uploaded document. Default 250000.</summary>
    int KnowledgeMaxExtractedChars { get; }

    /// <summary>Smallest chunk size a client may choose (tokens). Default 50.</summary>
    int KnowledgeMinChunkSizeTokens { get; }

    /// <summary>Largest chunk size a client may choose (tokens). Default 1000.</summary>
    int KnowledgeMaxChunkSizeTokens { get; }

    /// <summary>Smallest number of search results a client may choose. Default 1.</summary>
    int KnowledgeMinTopK { get; }

    /// <summary>Largest number of search results a client may choose. Default 10.</summary>
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

    /// <summary>How many websites one client may add. Default 25.</summary>
    int WebScrapingMaxSourcesPerClient { get; }

    /// <summary>Development environment only: comma-separated host:port entries the crawler may reach despite the SSRF rules (e.g. a local test site). Ignored outside Development. Default (empty).</summary>
    string WebScrapingDevAllowedHosts { get; }

    /// <summary>The crawler's User-Agent header. Default OmniBot/1.0 (+https://sculptflowapp.onrender.com; client knowledge-base crawler).</summary>
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

    /// <summary>Days after a campaign reply within which a booked consultation is attributed to that campaign.</summary>
    int CampaignsBookingAttributionDays { get; }

    /// <summary>Seconds between campaign sender runs (start due scheduled campaigns, send the next batch of running ones). Default 60.</summary>
    int CampaignsSenderIntervalSeconds { get; }

    /// <summary>Most messages the campaign sender sends per campaign each run. Default 20.</summary>
    int CampaignsSendBatchSize { get; }

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

    // ---- Voice
    /// <summary>Largest voice note (bytes) the Inbox will download from the provider to play or forward. Default 10485760.</summary>
    int VoiceMaxBytes { get; }

    // ---- Dashboard
    /// <summary>Leads per page on the dashboard. Default 25.</summary>
    int DashboardLeadsPageSize { get; }

    // ---- Cache (lifetimes of the table caches, read by CacheManager.LifetimeOf)
    /// <summary>Client rows (signed-in client, the bot's client info). Lifetime in minutes. Default 60.</summary>
    int CacheClientsMinutes { get; }
    /// <summary>Which client a signed-in user belongs to. Keep it short: it is how long a removed staff member could keep access if a removal were ever missed. Lifetime in minutes. Default 10.</summary>
    int CacheStaffLookupMinutes { get; }
    /// <summary>A client's services. Lifetime in minutes. Default 60.</summary>
    int CacheServicesMinutes { get; }
    /// <summary>Weekly hours, booking settings, closed dates and calendar connections. Lifetime in minutes. Default 60.</summary>
    int CacheSchedulingMinutes { get; }
    /// <summary>Channel connections, TikTok connection and WhatsApp templates. Lifetime in minutes. Default 60.</summary>
    int CacheChannelsMinutes { get; }
    /// <summary>Rating cards, rates and who pays the provider per channel account. Lifetime in minutes. Default 60.</summary>
    int CacheBillingRatesMinutes { get; }
    /// <summary>Business types with their stages and words (every dashboard page reads the signed-in client's). Lifetime in minutes. Default 60.</summary>
    int CacheBusinessTypesMinutes { get; }

    // ---- Monitoring (API timings: RequestTimingMiddleware, ApiTimingFlushJob)
    /// <summary>Whether request timings are recorded and slow requests logged. Default true.</summary>
    bool MonitoringEnabled { get; }
    /// <summary>A request at least this slow (ms) is logged as a warning and counted as slow. Default 1000.</summary>
    int MonitoringSlowRequestMs { get; }
    /// <summary>Days of per-minute API timing rows kept. Default 30.</summary>
    int MonitoringRetentionDays { get; }

    // ---- Error log (activity.app_logs: DatabaseLoggerProvider, AppLogFlushJob)
    /// <summary>Whether logged warnings/errors are also saved to activity.app_logs. Default true.</summary>
    bool ErrorLogEnabled { get; }
    /// <summary>Lowest level saved ("Warning", "Error" or "Critical", as written). Default "Warning".</summary>
    string ErrorLogMinimumLevel { get; }
    /// <summary>Days saved warnings are kept. Default 14.</summary>
    int ErrorLogWarningRetentionDays { get; }
    /// <summary>Days saved errors and criticals are kept. Default 90.</summary>
    int ErrorLogErrorRetentionDays { get; }
    /// <summary>Most rows saved per minute per fingerprint; the rest become one repeat-count row. Default 20.</summary>
    int ErrorLogMaxPerFingerprintPerMinute { get; }
    /// <summary>Comma-separated logger name prefixes never saved. Default empty.</summary>
    string ErrorLogExcludedCategories { get; }

    // ---- Message trace (activity.message_trace_steps: MessageTracer, MessageTraceFlushJob)
    /// <summary>Whether message steps are recorded. Default true.</summary>
    bool MessageTraceEnabled { get; }
    /// <summary>Days trace steps are kept. Default 14.</summary>
    int MessageTraceRetentionDays { get; }
    /// <summary>Days a trace with a failed step is kept. Default 60.</summary>
    int MessageTraceFailedRetentionDays { get; }

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
