using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Configuration;

namespace Omni.App.Common.Statics;

/// <summary>
/// Every setting read through IConfigManager, with its constant default, type and allowed range. A row in config.settings
/// with the same section and key replaces the default; without a row the default applies. Only settings declared here
/// can be stored (the settings API refuses anything else). To add one, follow "Configuration" in CLAUDE.md: declare it
/// here, list it in <see cref="All"/>, add its typed property to IConfigManager/ConfigManager, and read it through that
/// property. Never secrets: those stay in user-secrets / environment variables.
/// </summary>
public static class ConfigDefaults
{
    public static readonly ConfigDefinition AccountsSignupEnabled = new("Accounts", "SignupEnabled", "true", ConfigValueType.Bool,
        "Whether anyone can create a new client from the Register page or \"Continue with Google\". Off = only existing accounts can sign in.");
    public static readonly ConfigDefinition AccountsSignupsPerIpPerHour = new("Accounts", "SignupsPerIpPerHour", "20", ConfigValueType.Int,
        "Most sign-up attempts one IP address may make per hour (Register and the Google client-name step).", 1, 1000);
    public static readonly ConfigDefinition AccountsResetLinkMinutes = new("Accounts", "ResetLinkMinutes", "60", ConfigValueType.Int,
        "How long a password reset link works (minutes). Each link works once.", 10, 1440);
    public static readonly ConfigDefinition AccountsResetEmailsPerHour = new("Accounts", "ResetEmailsPerHour", "3", ConfigValueType.Int,
        "Most password reset emails one account gets per hour; further requests get the same answer but send nothing.", 1, 20);
    public static readonly ConfigDefinition AccountsInviteDays = new("Accounts", "InviteDays", "7", ConfigValueType.Int,
        "How long a member invite link works (days).", 1, 30);
    public static readonly ConfigDefinition EmailFromAddress = new("Email", "FromAddress", "", ConfigValueType.String,
        "Sender address of every email (password resets, invites). It must belong to a domain verified in Brevo. Empty = emails can't be sent through Brevo.");
    public static readonly ConfigDefinition EmailFromName = new("Email", "FromName", "Omni", ConfigValueType.String,
        "Sender name people see on Omni's emails.");
    public static readonly ConfigDefinition AvailabilityDefaultDurationMinutes = new("Availability", "DefaultDurationMinutes", "30", ConfigValueType.Int,
        "Booking length (minutes) for clients that haven't set their own booking settings.", 5, 480);
    public static readonly ConfigDefinition AvailabilityDefaultBufferMinutes = new("Availability", "DefaultBufferMinutes", "0", ConfigValueType.Int,
        "Gap (minutes) between bookings for clients that haven't set their own booking settings.", 0, 240);
    public static readonly ConfigDefinition AvailabilityDefaultNoticeMinutes = new("Availability", "DefaultNoticeMinutes", "240", ConfigValueType.Int,
        "Minimum notice (minutes) before a bookable slot, for clients that haven't set their own.", 0, 10080);
    public static readonly ConfigDefinition AvailabilityDefaultHorizonDays = new("Availability", "DefaultHorizonDays", "60", ConfigValueType.Int,
        "How many days ahead contacts can book, for clients that haven't set their own.", 1, 365);
    public static readonly ConfigDefinition AvailabilityMaxRangeDays = new("Availability", "MaxRangeDays", "14", ConfigValueType.Int,
        "The most days one free-slots request may cover.", 1, 60);
    public static readonly ConfigDefinition BillingEnabled = new("Billing", "Enabled", "true", ConfigValueType.Bool,
        "Master switch for usage billing and plan entitlements. Off = nothing is rated or reserved and every client may use everything.");
    public static readonly ConfigDefinition BillingGracePeriodDays = new("Billing", "GracePeriodDays", "7", ConfigValueType.Int,
        "How long a past-due subscription keeps working before it expires.", 0, 60);
    public static readonly ConfigDefinition BillingReservationTimeoutHours = new("Billing", "ReservationTimeoutHours", "72", ConfigValueType.Int,
        "A message charge still waiting for its delivery outcome is settled or released after this long.", 1, 720);
    public static readonly ConfigDefinition BillingMaintenanceIntervalMinutes = new("Billing", "MaintenanceIntervalMinutes", "5", ConfigValueType.Int,
        "How often the billing worker renews due subscriptions and resolves stale reservations.", 1, 60);
    public static readonly ConfigDefinition BillingSignupPlanCode = new("Billing", "SignupPlanCode", "trial", ConfigValueType.String,
        "Plan code a newly registered client starts on (first period free; the seeded Free Trial is \"trial\"). Empty = no plan until an admin assigns one.");
    public static readonly ConfigDefinition BillingCurrency = new("Billing", "Currency", "USD", ConfigValueType.String,
        "The one account currency (ISO 4217, e.g. USD). Balances, plans and rates are stored in it: change it only before any money has moved.", Pattern: @"^[A-Za-z]{3}$");
    public static readonly ConfigDefinition BillingRateBackdateToleranceMinutes = new("Billing", "RateBackdateToleranceMinutes", "5", ConfigValueType.Int,
        "How far in the past a new rate may start or an old one may end (clock skew allowance).", 0, 1440);
    public static readonly ConfigDefinition BillingRecentTransactions = new("Billing", "RecentTransactions", "15", ConfigValueType.Int,
        "How many recent wallet transactions a client's billing page shows.", 1, 100);
    public static readonly ConfigDefinition BillingChargeAiReplies = new("Billing", "ChargeAiReplies", "true", ConfigValueType.Bool,
        "Charge each AI assistant reply as ai_reply usage (included credit first, then wallet). Needs an ai_reply rate; a client that can't pay gets no AI reply and the conversation goes to the team. Off = AI replies are not charged.");
    public static readonly ConfigDefinition KnowledgeMinScore = new("Knowledge", "MinScore", "0.30", ConfigValueType.Decimal,
        "Minimum similarity for a search result, for new clients (each client can change its own).", 0, 1);
    public static readonly ConfigDefinition KnowledgeChunkMaxChars = new("Knowledge", "ChunkMaxChars", "1000", ConfigValueType.Int,
        "Chunk size in characters for new clients (stored per client in tokens).", 200, 4000);
    public static readonly ConfigDefinition KnowledgeChunkOverlapChars = new("Knowledge", "ChunkOverlapChars", "150", ConfigValueType.Int,
        "Chunk overlap in characters for new clients.", 0, 1000);
    public static readonly ConfigDefinition KnowledgeChunkMinChars = new("Knowledge", "ChunkMinChars", "200", ConfigValueType.Int,
        "A trailing piece shorter than this is merged into the previous chunk.", 1, 2000);
    public static readonly ConfigDefinition KnowledgeMaxUploadBytes = new("Knowledge", "MaxUploadBytes", "5242880", ConfigValueType.Int,
        "Largest knowledge-base file a client can upload (bytes).", 1024, 26214400);
    public static readonly ConfigDefinition KnowledgeMaxExtractedChars = new("Knowledge", "MaxExtractedChars", "250000", ConfigValueType.Int,
        "Most text kept from one uploaded document.", 1000, 2000000);
    public static readonly ConfigDefinition KnowledgeMinChunkSizeTokens = new("Knowledge", "MinChunkSizeTokens", "50", ConfigValueType.Int,
        "Smallest chunk size a client may choose (tokens).", 10, 1000);
    public static readonly ConfigDefinition KnowledgeMaxChunkSizeTokens = new("Knowledge", "MaxChunkSizeTokens", "1000", ConfigValueType.Int,
        "Largest chunk size a client may choose (tokens).", 50, 4000);
    public static readonly ConfigDefinition KnowledgeMinTopK = new("Knowledge", "MinTopK", "1", ConfigValueType.Int,
        "Smallest number of search results a client may choose.", 1, 50);
    public static readonly ConfigDefinition KnowledgeMaxTopK = new("Knowledge", "MaxTopK", "10", ConfigValueType.Int,
        "Largest number of search results a client may choose.", 1, 50);
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
    public static readonly ConfigDefinition WebScrapingMaxSourcesPerClient = new("WebScraping", "MaxSourcesPerClient", "25", ConfigValueType.Int,
        "How many websites one client may add.", 1, 200);
    public static readonly ConfigDefinition WebScrapingDevAllowedHosts = new("WebScraping", "DevAllowedHosts", "", ConfigValueType.String,
        "Development environment only: comma-separated host:port entries the crawler may reach despite the SSRF rules (e.g. a local test site). Ignored outside Development.");
    public static readonly ConfigDefinition WebScrapingUserAgent = new("WebScraping", "UserAgent", "OmniBot/1.0 (+https://sculptflowapp.onrender.com; client knowledge-base crawler)", ConfigValueType.String,
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
    public static readonly ConfigDefinition CampaignsBookingAttributionDays = new("Campaigns", "BookingAttributionDays", "14", ConfigValueType.Int,
        "A booking made this many days after a contact replied to a campaign counts as that campaign's booking.", 1, 365);
    public static readonly ConfigDefinition CampaignsSenderIntervalSeconds = new("Campaigns", "SenderIntervalSeconds", "60", ConfigValueType.Int,
        "How often the campaign sender starts campaigns whose scheduled time has come and sends the next batch of every running campaign.", 10, 3600);
    public static readonly ConfigDefinition CampaignsSendBatchSize = new("Campaigns", "SendBatchSize", "20", ConfigValueType.Int,
        "Most messages the campaign sender sends per campaign each time it runs.", 1, 500);
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
    public static readonly ConfigDefinition AiBotEngine = new("Ai", "BotEngine", "n8n", ConfigValueType.String,
        "Which engine answers contacts and the bot test panel: n8n (the AI assistant workflow at N8n:AiWebhookUrl) or code (BotEngine in the app). Read for every message, so a change applies within the settings refresh.", Pattern: @"^(n8n|code)$");
    public static readonly ConfigDefinition AiBaseUrl = new("Ai", "BaseUrl", "https://api.openai.com/v1", ConfigValueType.String,
        "Base URL of the OpenAI Responses API the AI bot and the benchmark question generator call.", Pattern: @"^https?://\S+$");
    public static readonly ConfigDefinition AiApiKey = new("Ai", "ApiKey", "", ConfigValueType.String,
        "SECRET. API key of the AI model API (bot replies, result wording, benchmark questions). Empty = the Embeddings / ApiKey is used. Never shown back: the Configuration page and API only say whether it is set.", IsSecret: true);
    public static readonly ConfigDefinition AiBotModel = new("Ai", "BotModel", "gpt-6-luna", ConfigValueType.String,
        "Model of the AI bot's agent (reads the contact's message, calls tools, decides the action).", Pattern: @"^\S+$");
    public static readonly ConfigDefinition AiFormatterModel = new("Ai", "FormatterModel", "gpt-4o-mini", ConfigValueType.String,
        "Model that words a booking or cancellation result as the bot's reply.", Pattern: @"^\S+$");
    public static readonly ConfigDefinition AiMaxToolIterations = new("Ai", "MaxToolIterations", "10", ConfigValueType.Int,
        "Most rounds of tool calls the agent may make for one contact message before the run stops.", 1, 50);
    public static readonly ConfigDefinition AiMemoryExchanges = new("Ai", "MemoryExchanges", "10", ConfigValueType.Int,
        "Earlier exchanges (contact message + bot output) of the conversation the agent sees.", 0, 100);
    public static readonly ConfigDefinition AiTimeoutSeconds = new("Ai", "TimeoutSeconds", "60", ConfigValueType.Int,
        "Timeout of one call to the AI model API.", 5, 600);
    public static readonly ConfigDefinition AiMaxConcurrentRuns = new("Ai", "MaxConcurrentRuns", "4", ConfigValueType.Int,
        "Most bot runs (different conversations) at the same time; one conversation always runs one message at a time.", 1, 64);
    public static readonly ConfigDefinition BenchmarkGeneratorModel = new("Benchmark", "GeneratorModel", "gpt-4o", ConfigValueType.String,
        "Model that writes the retrieval benchmark's questions.", Pattern: @"^\S+$");
    public static readonly ConfigDefinition IntegrationsOAuthStateLifetimeMinutes = new("Integrations", "OAuthStateLifetimeMinutes", "15", ConfigValueType.Int,
        "How long a calendar or TikTok connect link stays valid.", 1, 120);
    public static readonly ConfigDefinition IntegrationsTokenRefreshMarginMinutes = new("Integrations", "TokenRefreshMarginMinutes", "5", ConfigValueType.Int,
        "Calendar and TikTok access tokens are refreshed this long before they expire.", 0, 60);
    public static readonly ConfigDefinition VoiceMaxBytes = new("Voice", "MaxBytes", "10485760", ConfigValueType.Int,
        "Largest voice note (bytes) the Inbox will download from the provider to play or forward.", 1024, 20971520);
    public static readonly ConfigDefinition LocalizationLanguages = new("Localization", "Languages", "en,ar,fr", ConfigValueType.String,
        "Comma-separated languages the dashboard is offered and shown in (en, ar, fr). English is always on. A user or client whose language is turned off sees the next choice (the client's default, the browser, English).", Pattern: @"^s*[a-z]{2}(s*,s*[a-z]{2})*s*$");
    public static readonly ConfigDefinition LocalizationLanguageCookieDays = new("Localization", "LanguageCookieDays", "365", ConfigValueType.Int,
        "How long the language a visitor picks on the sign-in pages is remembered (the omni.lang cookie).", 1, 3650);
    public static readonly ConfigDefinition LocalizationShowTextKeys = new("Localization", "ShowTextKeys", "false", ConfigValueType.Bool,
        "Translation review mode: when on, opening any page with ?textKeys=1 shows each text's key after it (until ?textKeys=0). Never works in Production.");
    public static readonly ConfigDefinition DashboardContactsPageSize = new("Dashboard", "ContactsPageSize", "25", ConfigValueType.Int,
        "Contacts per page on the dashboard.", 5, 200);
    public static readonly ConfigDefinition CacheClientsMinutes = new("Cache", "ClientsMinutes", "60", ConfigValueType.Int,
        "Minutes a cached copy is kept: Client rows (signed-in client, the bot's client info). Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition CacheMembershipsMinutes = new("Cache", "MembershipsMinutes", "10", ConfigValueType.Int,
        "Minutes a cached copy is kept: Which client a signed-in user belongs to. Keep it short: it is how long a removed member could keep access if a removal were ever missed. Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition CacheServicesMinutes = new("Cache", "ServicesMinutes", "60", ConfigValueType.Int,
        "Minutes a cached copy is kept: A client's services. Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition CacheSchedulingMinutes = new("Cache", "SchedulingMinutes", "60", ConfigValueType.Int,
        "Minutes a cached copy is kept: Weekly hours, booking settings, closed dates and calendar connections. Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition CacheChannelsMinutes = new("Cache", "ChannelsMinutes", "60", ConfigValueType.Int,
        "Minutes a cached copy is kept: Channel connections, TikTok connection and WhatsApp templates. Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition CacheBillingRatesMinutes = new("Cache", "BillingRatesMinutes", "60", ConfigValueType.Int,
        "Minutes a cached copy is kept: Rating cards, rates and who pays the provider per channel account. Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition CacheBusinessTypesMinutes = new("Cache", "BusinessTypesMinutes", "60", ConfigValueType.Int,
        "Minutes a cached copy is kept: Business types with their pipeline stages and words. Changes clear it at once; this is only how long an untouched copy lives.", 1, 1440);
    public static readonly ConfigDefinition MonitoringEnabled = new("Monitoring", "Enabled", "true", ConfigValueType.Bool,
        "Whether request timings are recorded (per route, summarised every minute into activity.api_route_stats) and slow requests are logged. Off = nothing is recorded.");
    public static readonly ConfigDefinition MonitoringSlowRequestMs = new("Monitoring", "SlowRequestMs", "1000", ConfigValueType.Int,
        "A request taking at least this many milliseconds is logged as a warning and counted as slow.", 50, 120000);
    public static readonly ConfigDefinition MonitoringRetentionDays = new("Monitoring", "RetentionDays", "30", ConfigValueType.Int,
        "Days of per-minute API timing rows kept; older rows are deleted by the flush job.", 1, 365);

    // Error log (activity.app_logs): DatabaseLoggerProvider, AppLogCollector, AppLogFlushJob.
    public static readonly ConfigDefinition ErrorLogEnabled = new("ErrorLog", "Enabled", "true", ConfigValueType.Bool,
        "Whether warnings and errors the app logs are also saved to activity.app_logs (the admin portal's Errors page). Off = only the console log.");
    public static readonly ConfigDefinition ErrorLogMinimumLevel = new("ErrorLog", "MinimumLevel", "Warning", ConfigValueType.String,
        "Lowest level saved: Warning, Error or Critical.", Pattern: "^(?i:warning|error|critical)$");
    public static readonly ConfigDefinition ErrorLogWarningRetentionDays = new("ErrorLog", "WarningRetentionDays", "14", ConfigValueType.Int,
        "Days saved warnings are kept; older ones are deleted by the flush job.", 1, 365);
    public static readonly ConfigDefinition ErrorLogErrorRetentionDays = new("ErrorLog", "ErrorRetentionDays", "90", ConfigValueType.Int,
        "Days saved errors and criticals are kept; older ones are deleted by the flush job.", 1, 365);
    public static readonly ConfigDefinition ErrorLogMaxPerFingerprintPerMinute = new("ErrorLog", "MaxPerFingerprintPerMinute", "20", ConfigValueType.Int,
        "Most rows saved per minute for one problem (same place, template and exception); the rest of that minute is saved as one row with a repeat count.", 1, 1000);
    public static readonly ConfigDefinition ErrorLogExcludedCategories = new("ErrorLog", "ExcludedCategories", "", ConfigValueType.String,
        "Comma-separated logger name prefixes never saved (e.g. a noisy framework warning). Empty = none.");

    // Message trace (activity.message_trace_steps): MessageTracer, MessageTraceFlushJob.
    public static readonly ConfigDefinition MessageTraceEnabled = new("MessageTrace", "Enabled", "true", ConfigValueType.Bool,
        "Whether each step a message takes (webhook, inbox, bot, send) is recorded for the admin portal's message trace. Off = nothing new is recorded.");
    public static readonly ConfigDefinition MessageTraceRetentionDays = new("MessageTrace", "RetentionDays", "14", ConfigValueType.Int,
        "Days trace steps are kept; older ones are deleted by the flush job (traces with a failed step follow FailedRetentionDays).", 1, 365);
    public static readonly ConfigDefinition MessageTraceFailedRetentionDays = new("MessageTrace", "FailedRetentionDays", "60", ConfigValueType.Int,
        "Days a trace with at least one failed step is kept (when longer than RetentionDays).", 1, 365);

    public static readonly ConfigDefinition OptOutEnabled = new("OptOut", "Enabled", "true", ConfigValueType.Bool,
        "Whether a contact's message that is only a stop or start word opts them out of (or back into) business-started messages: campaigns, reminders and Inbox templates.");
    public static readonly ConfigDefinition OptOutStopKeywords = new("OptOut", "StopKeywords", "stop,stopall,unsubscribe,arret,desabonner,desinscrire,توقف,ايقاف,الغاء الاشتراك", ConfigValueType.String,
        "Words that opt a contact out when they are the whole message (case, accents, Arabic diacritics and end punctuation ignored).");
    public static readonly ConfigDefinition OptOutStartKeywords = new("OptOut", "StartKeywords", "start,unstop,subscribe,demarrer,اشتراك", ConfigValueType.String,
        "Words that opt a contact back in when they are the whole message (same matching as StopKeywords).");
    public static readonly ConfigDefinition OptOutSendConfirmation = new("OptOut", "SendConfirmation", "true", ConfigValueType.Bool,
        "Reply once to a stop or start word with a short confirmation in the contact's language (a normal outbound message, billed like any other).");

    public static readonly ConfigDefinition AutoReplyEnabled = new("AutoReply", "Enabled", "true", ConfigValueType.Bool,
        "Master switch for automatic replies to new and reopened conversations. Off = none is sent for any client (each client also sets its own per channel).");
    public static readonly ConfigDefinition AutoReplyMaxLength = new("AutoReply", "MaxLength", "1000", ConfigValueType.Int,
        "Longest automatic reply a client may save, in characters.", 1, 4096);

    public static readonly ConfigDefinition BotsInstructionsMaxLength = new("Bots", "InstructionsMaxLength", "4000", ConfigValueType.Int,
        "Longest instructions a custom AI bot may have, in characters.", 100, 20000);
    public static readonly ConfigDefinition BotsHandoffMessageMaxLength = new("Bots", "HandoffMessageMaxLength", "500", ConfigValueType.Int,
        "Longest handoff message an AI bot may send when it hands a conversation to the team, in characters.", 1, 4096);
    public static readonly ConfigDefinition BotsTestMessageMaxLength = new("Bots", "TestMessageMaxLength", "1000", ConfigValueType.Int,
        "Longest message a member may send a bot in its test conversation, in characters.", 1, 4096);
    public static readonly ConfigDefinition BotsTestSessionHours = new("Bots", "TestSessionHours", "24", ConfigValueType.Int,
        "Bot test conversations untouched for this many hours are deleted when a new one starts.", 1, 720);

    public static readonly ConfigDefinition RemindersEnabled = new("Reminders", "Enabled", "true", ConfigValueType.Bool,
        "Master switch for booking reminders. Off = the reminder worker sends nothing for any client (each client also turns its own reminders on).");
    public static readonly ConfigDefinition RemindersIntervalSeconds = new("Reminders", "IntervalSeconds", "60", ConfigValueType.Int,
        "How often the reminder worker looks for due booking reminders.", 15, 3600);
    public static readonly ConfigDefinition RemindersBatchSize = new("Reminders", "BatchSize", "50", ConfigValueType.Int,
        "Most reminders of one kind the worker handles for one client per run (the rest wait for the next run).", 1, 500);

    public static readonly IReadOnlyList<ConfigDefinition> All =
    [
        AccountsSignupEnabled,
        AccountsSignupsPerIpPerHour,
        AccountsResetLinkMinutes,
        AccountsResetEmailsPerHour,
        AccountsInviteDays,

        EmailFromAddress,
        EmailFromName,

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
        BillingChargeAiReplies,

        OptOutEnabled,
        OptOutStopKeywords,
        OptOutStartKeywords,
        OptOutSendConfirmation,

        AutoReplyEnabled,
        AutoReplyMaxLength,

        BotsInstructionsMaxLength,
        BotsHandoffMessageMaxLength,
        BotsTestMessageMaxLength,
        BotsTestSessionHours,

        RemindersEnabled,
        RemindersIntervalSeconds,
        RemindersBatchSize,

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
        WebScrapingMaxSourcesPerClient,
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
        CampaignsBookingAttributionDays,
        CampaignsSenderIntervalSeconds,
        CampaignsSendBatchSize,

        EmbeddingsBaseUrl,
        EmbeddingsModel,
        EmbeddingsDimensions,
        EmbeddingsApiKey,
        EmbeddingsMaxInputsPerRequest,

        AiBotEngine,
        AiBaseUrl,
        AiApiKey,
        AiBotModel,
        AiFormatterModel,
        AiMaxToolIterations,
        AiMemoryExchanges,
        AiTimeoutSeconds,
        AiMaxConcurrentRuns,
        BenchmarkGeneratorModel,

        IntegrationsOAuthStateLifetimeMinutes,
        IntegrationsTokenRefreshMarginMinutes,

        VoiceMaxBytes,

        DashboardContactsPageSize,

        LocalizationLanguages,
        LocalizationLanguageCookieDays,
        LocalizationShowTextKeys,

        CacheClientsMinutes,
        CacheMembershipsMinutes,
        CacheServicesMinutes,
        CacheSchedulingMinutes,
        CacheChannelsMinutes,
        CacheBillingRatesMinutes,
        CacheBusinessTypesMinutes,

        MonitoringEnabled,
        MonitoringSlowRequestMs,
        MonitoringRetentionDays,
        ErrorLogEnabled,
        ErrorLogMinimumLevel,
        ErrorLogWarningRetentionDays,
        ErrorLogErrorRetentionDays,
        ErrorLogMaxPerFingerprintPerMinute,
        ErrorLogExcludedCategories,
        MessageTraceEnabled,
        MessageTraceRetentionDays,
        MessageTraceFailedRetentionDays
    ];

    /// <summary>The declared setting for (section, key), ignoring case; null when there is none.</summary>
    public static ConfigDefinition? Find(string section, string key) =>
        All.FirstOrDefault(d => string.Equals(d.Section, section, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}
