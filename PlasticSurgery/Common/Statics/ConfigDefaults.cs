using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Configuration;

namespace PlasticSurgery.Common.Statics;

/// <summary>
/// Every setting read through IConfigManager, with its constant default, type and allowed range. A row in config.settings
/// with the same section and key replaces the default; without a row the default applies. Only settings declared here
/// can be stored (the settings API refuses anything else). To add one, follow "Configuration" in CLAUDE.md: declare it
/// here, list it in <see cref="All"/>, add its typed property to IConfigManager/ConfigManager, and read it through that
/// property. Never secrets: those stay in user-secrets / environment variables.
/// </summary>
public static class ConfigDefaults
{
    public static readonly ConfigDefinition AvailabilityDefaultDurationMinutes = new("Availability", "DefaultDurationMinutes", "30", ConfigValueType.Int,
        "Consultation length (minutes) for clinics that haven't set their own booking settings.", 5, 480);
    public static readonly ConfigDefinition AvailabilityDefaultBufferMinutes = new("Availability", "DefaultBufferMinutes", "0", ConfigValueType.Int,
        "Gap (minutes) between consultations for clinics that haven't set their own booking settings.", 0, 240);
    public static readonly ConfigDefinition AvailabilityDefaultNoticeMinutes = new("Availability", "DefaultNoticeMinutes", "240", ConfigValueType.Int,
        "Minimum notice (minutes) before a bookable slot, for clinics that haven't set their own.", 0, 10080);
    public static readonly ConfigDefinition AvailabilityDefaultHorizonDays = new("Availability", "DefaultHorizonDays", "60", ConfigValueType.Int,
        "How many days ahead patients can book, for clinics that haven't set their own.", 1, 365);
    public static readonly ConfigDefinition AvailabilityMaxRangeDays = new("Availability", "MaxRangeDays", "14", ConfigValueType.Int,
        "The most days one free-slots request may cover.", 1, 60);
    public static readonly ConfigDefinition BillingEnabled = new("Billing", "Enabled", "true", ConfigValueType.Bool,
        "Master switch for usage billing and plan entitlements. Off = nothing is rated or reserved and every clinic may use everything.");
    public static readonly ConfigDefinition BillingGracePeriodDays = new("Billing", "GracePeriodDays", "7", ConfigValueType.Int,
        "How long a past-due subscription keeps working before it expires.", 0, 60);
    public static readonly ConfigDefinition BillingReservationTimeoutHours = new("Billing", "ReservationTimeoutHours", "72", ConfigValueType.Int,
        "A message charge still waiting for its delivery outcome is settled or released after this long.", 1, 720);
    public static readonly ConfigDefinition BillingMaintenanceIntervalMinutes = new("Billing", "MaintenanceIntervalMinutes", "5", ConfigValueType.Int,
        "How often the billing worker renews due subscriptions and resolves stale reservations.", 1, 60);
    public static readonly ConfigDefinition BillingSignupPlanCode = new("Billing", "SignupPlanCode", "", ConfigValueType.String,
        "Plan code a newly registered clinic starts on (first period free). Empty = no plan until an admin assigns one.");
    public static readonly ConfigDefinition BillingCurrency = new("Billing", "Currency", "USD", ConfigValueType.String,
        "The one account currency (ISO 4217, e.g. USD). Balances, plans and rates are stored in it: change it only before any money has moved.", Pattern: @"^[A-Za-z]{3}$");
    public static readonly ConfigDefinition BillingRateBackdateToleranceMinutes = new("Billing", "RateBackdateToleranceMinutes", "5", ConfigValueType.Int,
        "How far in the past a new rate may start or an old one may end (clock skew allowance).", 0, 1440);
    public static readonly ConfigDefinition BillingRecentTransactions = new("Billing", "RecentTransactions", "15", ConfigValueType.Int,
        "How many recent wallet transactions a clinic's billing page shows.", 1, 100);
    public static readonly ConfigDefinition KnowledgeMinScore = new("Knowledge", "MinScore", "0.30", ConfigValueType.Decimal,
        "Minimum similarity for a search result, for new clinics (each clinic can change its own).", 0, 1);
    public static readonly ConfigDefinition KnowledgeChunkMaxChars = new("Knowledge", "ChunkMaxChars", "1000", ConfigValueType.Int,
        "Chunk size in characters for new clinics (stored per clinic in tokens).", 200, 4000);
    public static readonly ConfigDefinition KnowledgeChunkOverlapChars = new("Knowledge", "ChunkOverlapChars", "150", ConfigValueType.Int,
        "Chunk overlap in characters for new clinics.", 0, 1000);
    public static readonly ConfigDefinition KnowledgeChunkMinChars = new("Knowledge", "ChunkMinChars", "200", ConfigValueType.Int,
        "A trailing piece shorter than this is merged into the previous chunk.", 1, 2000);
    public static readonly ConfigDefinition KnowledgeMaxUploadBytes = new("Knowledge", "MaxUploadBytes", "5242880", ConfigValueType.Int,
        "Largest knowledge-base file a clinic can upload (bytes).", 1024, 26214400);
    public static readonly ConfigDefinition KnowledgeMaxExtractedChars = new("Knowledge", "MaxExtractedChars", "250000", ConfigValueType.Int,
        "Most text kept from one uploaded document.", 1000, 2000000);
    public static readonly ConfigDefinition KnowledgeMinChunkSizeTokens = new("Knowledge", "MinChunkSizeTokens", "50", ConfigValueType.Int,
        "Smallest chunk size a clinic may choose (tokens).", 10, 1000);
    public static readonly ConfigDefinition KnowledgeMaxChunkSizeTokens = new("Knowledge", "MaxChunkSizeTokens", "1000", ConfigValueType.Int,
        "Largest chunk size a clinic may choose (tokens).", 50, 4000);
    public static readonly ConfigDefinition KnowledgeMinTopK = new("Knowledge", "MinTopK", "1", ConfigValueType.Int,
        "Smallest number of search results a clinic may choose.", 1, 50);
    public static readonly ConfigDefinition KnowledgeMaxTopK = new("Knowledge", "MaxTopK", "10", ConfigValueType.Int,
        "Largest number of search results a clinic may choose.", 1, 50);
    public static readonly ConfigDefinition WebScrapingMaxPages = new("WebScraping", "MaxPages", "100", ConfigValueType.Int,
        "Most distinct pages one website crawl visits.", 1, 500);
    public static readonly ConfigDefinition WebScrapingMaxDepth = new("WebScraping", "MaxDepth", "3", ConfigValueType.Int,
        "Link depth from the start page.", 0, 6);
    public static readonly ConfigDefinition WebScrapingMaxConcurrency = new("WebScraping", "MaxConcurrency", "3", ConfigValueType.Int,
        "Simultaneous requests to one website.", 1, 6);
    public static readonly ConfigDefinition WebScrapingRequestTimeoutSeconds = new("WebScraping", "RequestTimeoutSeconds", "15", ConfigValueType.Int,
        "Timeout of one page request.", 2, 60);
    public static readonly ConfigDefinition WebScrapingMaxResponseBytes = new("WebScraping", "MaxResponseBytes", "2000000", ConfigValueType.Int,
        "Largest page the crawler reads (bytes, after decompression).", 50000, 10000000);
    public static readonly ConfigDefinition WebScrapingMaxRedirects = new("WebScraping", "MaxRedirects", "5", ConfigValueType.Int,
        "Redirects followed for one page.", 0, 10);
    public static readonly ConfigDefinition WebScrapingPolitenessDelayMs = new("WebScraping", "PolitenessDelayMs", "300", ConfigValueType.Int,
        "Pause before each request (robots.txt Crawl-delay can raise it).", 0, 5000);
    public static readonly ConfigDefinition WebScrapingMaxRunMinutes = new("WebScraping", "MaxRunMinutes", "20", ConfigValueType.Int,
        "Wall-clock limit of one crawl.", 1, 120);
    public static readonly ConfigDefinition WebScrapingMinTextChars = new("WebScraping", "MinTextChars", "80", ConfigValueType.Int,
        "Pages with less readable text are skipped.", 1, 5000);
    public static readonly ConfigDefinition WebScrapingMaxTextChars = new("WebScraping", "MaxTextChars", "200000", ConfigValueType.Int,
        "Most text kept from one page.", 1000, 1000000);
    public static readonly ConfigDefinition WebScrapingMaxQueryVariantsPerPath = new("WebScraping", "MaxQueryVariantsPerPath", "5", ConfigValueType.Int,
        "Different query strings allowed for one path (stops calendar and filter traps).", 1, 50);
    public static readonly ConfigDefinition WebScrapingMaxLinksPerPage = new("WebScraping", "MaxLinksPerPage", "300", ConfigValueType.Int,
        "Most links taken from one page.", 10, 2000);
    public static readonly ConfigDefinition WebScrapingMaxSourcesPerClinic = new("WebScraping", "MaxSourcesPerClinic", "25", ConfigValueType.Int,
        "How many websites one clinic may add.", 1, 200);
    public static readonly ConfigDefinition WebScrapingDevAllowedHosts = new("WebScraping", "DevAllowedHosts", "", ConfigValueType.String,
        "Development environment only: comma-separated host:port entries the crawler may reach despite the SSRF rules (e.g. a local test site). Ignored outside Development.");
    public static readonly ConfigDefinition WebScrapingUserAgent = new("WebScraping", "UserAgent", "SculptFlowBot/1.0 (+https://sculptflowapp.onrender.com; clinic knowledge-base crawler)", ConfigValueType.String,
        "The crawler's User-Agent header.", Pattern: @"^\S.*$");
    public static readonly ConfigDefinition BenchmarkGenerationSampleSize = new("Benchmark", "GenerationSampleSize", "20", ConfigValueType.Int,
        "Chunks sent to n8n per question-generation request.", 1, 200);
    public static readonly ConfigDefinition BenchmarkSamplePoolSize = new("Benchmark", "SamplePoolSize", "60", ConfigValueType.Int,
        "Chunks sampled to choose the generation chunks from.", 1, 1000);
    public static readonly ConfigDefinition BenchmarkMinSourceChunkChars = new("Benchmark", "MinSourceChunkChars", "80", ConfigValueType.Int,
        "Shortest chunk that may be used to generate a question.", 1, 5000);
    public static readonly ConfigDefinition BenchmarkMaxConsecutiveErrors = new("Benchmark", "MaxConsecutiveErrors", "5", ConfigValueType.Int,
        "A benchmark run stops after this many retrieval errors in a row.", 1, 100);
    public static readonly ConfigDefinition BenchmarkStaleRunHours = new("Benchmark", "StaleRunHours", "2", ConfigValueType.Int,
        "A run still marked running after this long is treated as abandoned.", 1, 48);
    public static readonly ConfigDefinition BenchmarkGenerationTimeoutMinutes = new("Benchmark", "GenerationTimeoutMinutes", "30", ConfigValueType.Int,
        "How long to wait for n8n to send generated questions.", 1, 240);
    public static readonly ConfigDefinition BenchmarkMaxQuestionsPerChunk = new("Benchmark", "MaxQuestionsPerChunk", "3", ConfigValueType.Int,
        "Most generated questions accepted per chunk.", 1, 10);
    public static readonly ConfigDefinition CampaignsDefaultInactiveDays = new("Campaigns", "DefaultInactiveDays", "60", ConfigValueType.Int,
        "Default \"inactive for\" days of a reactivation audience.", 1, 3650);
    public static readonly ConfigDefinition EmbeddingsBaseUrl = new("Embeddings", "BaseUrl", "https://api.openai.com/v1", ConfigValueType.String,
        "Base URL of the OpenAI-compatible embeddings API.", Pattern: @"^https?://\S+$");
    public static readonly ConfigDefinition EmbeddingsModel = new("Embeddings", "Model", "text-embedding-3-small", ConfigValueType.String,
        "Embedding model. Stored vectors are tied to it: changing it means re-embedding every knowledge document.", Pattern: @"^\S+$");
    public static readonly ConfigDefinition EmbeddingsDimensions = new("Embeddings", "Dimensions", "1536", ConfigValueType.Int,
        "Vector size the model returns. Must equal the vector(N) size of knowledge.knowledge_chunks.embedding (1536).", 1, 4096);
    public static readonly ConfigDefinition EmbeddingsApiKey = new("Embeddings", "ApiKey", "", ConfigValueType.String,
        "SECRET. API key of the embeddings API. Never shown back: the Configuration page and API only say whether it is set.", IsSecret: true);
    public static readonly ConfigDefinition EmbeddingsMaxInputsPerRequest = new("Embeddings", "MaxInputsPerRequest", "64", ConfigValueType.Int,
        "Texts sent to the embeddings API in one request.", 1, 2048);
    public static readonly ConfigDefinition IntegrationsOAuthStateLifetimeMinutes = new("Integrations", "OAuthStateLifetimeMinutes", "15", ConfigValueType.Int,
        "How long a calendar or TikTok connect link stays valid.", 1, 120);
    public static readonly ConfigDefinition IntegrationsTokenRefreshMarginMinutes = new("Integrations", "TokenRefreshMarginMinutes", "5", ConfigValueType.Int,
        "Calendar and TikTok access tokens are refreshed this long before they expire.", 0, 60);
    public static readonly ConfigDefinition InfobipTimeoutSeconds = new("Infobip", "TimeoutSeconds", "20", ConfigValueType.Int,
        "Timeout of one call to Infobip.", 5, 120);
    public static readonly ConfigDefinition InfobipMaxSendAttempts = new("Infobip", "MaxSendAttempts", "3", ConfigValueType.Int,
        "Total tries for a send Infobip clearly refused (429/503/no connection).", 1, 5);
    public static readonly ConfigDefinition DashboardLeadsPageSize = new("Dashboard", "LeadsPageSize", "25", ConfigValueType.Int,
        "Leads per page on the dashboard.", 5, 200);

    public static readonly IReadOnlyList<ConfigDefinition> All =
    [
        AvailabilityDefaultDurationMinutes,
        AvailabilityDefaultBufferMinutes,
        AvailabilityDefaultNoticeMinutes,
        AvailabilityDefaultHorizonDays,
        AvailabilityMaxRangeDays,

        BillingEnabled,
        BillingGracePeriodDays,
        BillingReservationTimeoutHours,
        BillingMaintenanceIntervalMinutes,
        BillingSignupPlanCode,
        BillingCurrency,
        BillingRateBackdateToleranceMinutes,
        BillingRecentTransactions,

        KnowledgeMinScore,
        KnowledgeChunkMaxChars,
        KnowledgeChunkOverlapChars,
        KnowledgeChunkMinChars,
        KnowledgeMaxUploadBytes,
        KnowledgeMaxExtractedChars,
        KnowledgeMinChunkSizeTokens,
        KnowledgeMaxChunkSizeTokens,
        KnowledgeMinTopK,
        KnowledgeMaxTopK,

        WebScrapingMaxPages,
        WebScrapingMaxDepth,
        WebScrapingMaxConcurrency,
        WebScrapingRequestTimeoutSeconds,
        WebScrapingMaxResponseBytes,
        WebScrapingMaxRedirects,
        WebScrapingPolitenessDelayMs,
        WebScrapingMaxRunMinutes,
        WebScrapingMinTextChars,
        WebScrapingMaxTextChars,
        WebScrapingMaxQueryVariantsPerPath,
        WebScrapingMaxLinksPerPage,
        WebScrapingMaxSourcesPerClinic,
        WebScrapingDevAllowedHosts,
        WebScrapingUserAgent,

        BenchmarkGenerationSampleSize,
        BenchmarkSamplePoolSize,
        BenchmarkMinSourceChunkChars,
        BenchmarkMaxConsecutiveErrors,
        BenchmarkStaleRunHours,
        BenchmarkGenerationTimeoutMinutes,
        BenchmarkMaxQuestionsPerChunk,

        CampaignsDefaultInactiveDays,

        EmbeddingsBaseUrl,
        EmbeddingsModel,
        EmbeddingsDimensions,
        EmbeddingsApiKey,
        EmbeddingsMaxInputsPerRequest,

        IntegrationsOAuthStateLifetimeMinutes,
        IntegrationsTokenRefreshMarginMinutes,

        InfobipTimeoutSeconds,
        InfobipMaxSendAttempts,

        DashboardLeadsPageSize
    ];

    /// <summary>The declared setting for (section, key), ignoring case; null when there is none.</summary>
    public static ConfigDefinition? Find(string section, string key) =>
        All.FirstOrDefault(d => string.Equals(d.Section, section, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}
