using Omni.App.Business.Contracts.HttpClients.Brevo;
using Omni.App.Business.Contracts.Providers.Email;
using Omni.App.Business.Contracts.Services.Accounts;
using Omni.App.Business.Contracts.Services.Email;
using Omni.App.Business.HttpClients.Brevo;
using Omni.App.Business.Providers.Email;
using Omni.App.Business.Services.Accounts;
using Omni.App.Business.Services.Email;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Services.Ai;
using Omni.App.Business.Services.BusinessTypes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Engines.Knowledge;
using Omni.App.Business.Contracts.Engines.KnowledgeBenchmark;
using Omni.App.Business.Contracts.Engines.Telegram;
using Omni.App.Business.Contracts.Engines.WebScraping;
using Omni.App.Business.Contracts.Engines.WhatsApp;
using Omni.App.Business.Contracts.HttpClients.Calendars;
using Omni.App.Business.Contracts.HttpClients.Meta;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.HttpClients.OpenAi;
using Omni.App.Business.Contracts.HttpClients.Telegram;
using Omni.App.Business.Contracts.HttpClients.TikTok;
using Omni.App.Business.Contracts.HttpClients.WebScraping;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Caching;
using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Contracts.Providers.Media;
using Omni.App.Business.Providers.Media;
using Omni.App.Business.Contracts.Services.Caching;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Providers.Logging;
using Omni.App.Business.Services.Caching;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Services.PlatformAdmin;
using Omni.App.Business.Contracts.Providers.WhatsApp;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.Automation;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Business.Contracts.Services.Channels;
using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Business.Contracts.Services.Configuration;
using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Business.Contracts.Services.Reports;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Contracts.Services.Orders;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Business.Contracts.Services.TikTok;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Business.Engines.Knowledge;
using Omni.App.Business.Engines.KnowledgeBenchmark;
using Omni.App.Business.Engines.Telegram;
using Omni.App.Business.Engines.WebScraping;
using Omni.App.Business.Engines.WhatsApp;
using Omni.App.Business.Engines.WhatsApp.Handlers;
using Omni.App.Business.HttpClients.Calendars;
using Omni.App.Business.HttpClients.Meta;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Business.HttpClients.OpenAi;
using Omni.App.Business.HttpClients.Telegram;
using Omni.App.Business.HttpClients.TikTok;
using Omni.App.Business.HttpClients.WebScraping;
using Omni.App.Business.Jobs;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Channels;
using Omni.App.Business.Providers.WhatsApp;
using Omni.App.Business.Services.Bookings;
using Omni.App.Business.Services.Automation;
using Omni.App.Business.Services.Calendars;
using Omni.App.Business.Services.Campaigns;
using Omni.App.Business.Services.Channels;
using Omni.App.Business.Services.Clients;
using Omni.App.Business.Services.Configuration;
using Omni.App.Business.Services.Dashboard;
using Omni.App.Business.Services.Reports;
using Omni.App.Business.Services.Inbox;
using Omni.App.Business.Services.Knowledge;
using Omni.App.Business.Services.Contacts;
using Omni.App.Business.Services.Notifications;
using Omni.App.Business.Services.Catalog;
using Omni.App.Business.Services.Orders;
using Omni.App.Business.Services.Members;
using Omni.App.Business.Services.TikTok;
using Omni.App.Business.Services.WhatsApp;
using Omni.App.Business.Engines.Admins;
using Omni.App.Common.Configs;
using Omni.App.Common.Extensions;
using Omni.App.Hubs;
using Omni.App.Middleware;
using Omni.App.Business.Contracts.Services.Monitoring;
using Omni.App.Business.Services.Monitoring;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Every thread parses and prints machine values (form fields, data-* numbers, URLs, JSON) the same way on every server;
// people see their language's dates and numbers through the per-request CurrentUICulture (RequestTextMiddleware,
// Common/Helpers/TextCulture).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = Omni.App.Common.Helpers.AppCulture.For(Omni.App.Common.Enums.AppLanguage.English);

// ---------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------
// The schema itself is owned by Database/schema.sql, not EF Core migrations
// (see ApplicationDbContext's doc comment) — this just points EF Core at an
// already-provisioned Postgres/Supabase database.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:Postgres. Set it in appsettings.Development.json " +
        "(local dev) or via user-secrets / environment variables (production, e.g. Supabase).");

// Every save clears the cache keys of the rows it changes (Persistence/Helpers/CacheInvalidationInterceptor).
builder.Services.AddSingleton<CacheInvalidationInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) => options.UseNpgsql(connectionString)
    .AddInterceptors(services.GetRequiredService<CacheInvalidationInterceptor>()));
// Repositories + unit of work (Persistence/), all sharing the request's DbContext.
builder.Services.AddPersistence();

// ---------------------------------------------------------------------
// Authentication — ASP.NET Core Identity over identity.users (AppUser). One users table for everyone; users.role
// (UserRole) decides the app: client and member users use this dashboard, admin users only the admin portal (which
// signs them in through POST /api/platform-admin/auth/login). AppSignInManager refuses anyone else at sign-in and
// CurrentClientContext.ValidatePrincipalAsync re-checks role, active user and active membership on every request.
// AddIdentityCore (not AddIdentity<TUser,TRole>) keeps Identity's role tables out: the role is a column. Cookie auth is
// wired up explicitly below. See Business/Contracts/Managers/ICurrentClientContext.cs for how a user resolves to a client.
// ---------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();

// Password rules, lockout (5 failed sign-ins lock for 15 minutes, the admin portal's old rule), AppSignInManager.
builder.Services.AddOmniIdentity();

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "omni.auth";
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        // Role, active user and active membership on every request (an admin or a removed member is signed out).
        options.Events.OnValidatePrincipal = CurrentClientContext.ValidatePrincipalAsync;
    })
    // Holds the Google identity for the few minutes between "Google said who this is" and "we signed them in
    // (or collected their client name first)" — AddIdentityCore doesn't register this scheme itself.
    .AddCookie(IdentityConstants.ExternalScheme, options => options.ExpireTimeSpan = TimeSpan.FromMinutes(10));

// "Continue with Google" (sign in / sign up) — only registered when both values are configured, so a server
// without them simply doesn't offer the button. Login only: it asks for openid/email/profile, never Calendar.
if (GoogleLoginSettings.IsEnabled(builder.Configuration))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["GoogleLogin:ClientId"]!;
        options.ClientSecret = builder.Configuration["GoogleLogin:ClientSecret"]!;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // Google's userinfo says whether it has verified the email; only a verified one is ever trusted.
        options.ClaimActions.MapJsonKey(GoogleLoginSettings.EmailVerifiedClaim, "email_verified");
        // Denied consent / a failed handshake lands back on the sign-in page with a fixed code, never a stack trace.
        options.Events.OnRemoteFailure = ctx =>
        {
            // The handler words a refusal as "Access was denied by the resource owner or by the remote server."
            var denied = ctx.Failure?.Message?.Contains("denied", StringComparison.OrdinalIgnoreCase) == true;
            ctx.Response.Redirect($"/Account/Login?externalError={(denied ? "cancelled" : "failed")}");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

// Behind Render's proxy the app only ever sees plain HTTP; trusting X-Forwarded-Proto makes Request.Scheme
// https again, which Google's redirect_uri (built from the request) and Secure cookies both depend on.
// X-Forwarded-For gives the real client IP for the sign-up rate limit; with the default ForwardLimit of 1 only the
// address Render itself appended (the last one) is used, so a client can't pick its own by sending the header.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAuthorization();

// Sign-up attempts per IP per hour (Accounts:SignupsPerIpPerHour); everything else is unlimited.
builder.Services.AddSignupRateLimiting();

// ---------------------------------------------------------------------
// Application services — shared by both the API controllers and the
// Razor Pages dashboard, so business logic and client-scoping live in
// exactly one place.
// ---------------------------------------------------------------------
builder.Services.AddScoped<IEventLogger, EventLogger>();
builder.Services.AddScoped<IClientContext, ClientContext>();
builder.Services.AddScoped<ICurrentClientContext, CurrentClientContext>();
builder.Services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
// Business types: the words, pipeline stages and AI profile of each kind of client (core.business_types, crm.pipeline_stages).
builder.Services.AddScoped<IBusinessTypeService, BusinessTypeService>();
// The client facts every AI trigger carries to n8n (name, business type, AI profile, words, language, timezone).
builder.Services.AddScoped<IAiProfileService, AiProfileService>();
// UI text: Resources/i18n/{language}.json (embedded) through IStringLocalizer (@T["key"] in Razor, [ApiErrors] for
// server messages) and window.omniText in the browser; RequestTextMiddleware picks each request's language and the
// client's business-type words.
builder.Services.AddSingleton<ITextCatalog, TextCatalog>();
builder.Services.AddScoped<ITerminology, Terminology>();
builder.Services.AddScoped<ITextContext, TextContext>();
builder.Services.AddScoped<IStringLocalizer, JsonStringLocalizer>();
builder.Services.AddScoped(typeof(IStringLocalizer<>), typeof(JsonStringLocalizer<>));
builder.Services.AddScoped<IClientProfileService, ClientProfileService>();
builder.Services.AddScoped<IInboxSettingsService, InboxSettingsService>();
builder.Services.AddScoped<IAutoReplyService, AutoReplyService>();
builder.Services.AddScoped<IMemberService, MemberService>();
// Member invites (Milestone C): emailed one-time links in identity.account_tokens; an open invite holds a seat.
builder.Services.AddScoped<IInviteService, InviteService>();
// Email (Milestone C): Brevo's transactional API when Brevo:ApiKey is set. Without a key, outside Production the email
// (link included) is written to the log instead; in Production BrevoEmailProvider then refuses, so nothing is dropped
// silently. Account emails are worded in the reader's language (IReaderTextFactory).
builder.Services.AddHttpClient<IBrevoApiClient, BrevoApiClient>();
builder.Services.AddScoped<BrevoEmailProvider>();
builder.Services.AddScoped<LogEmailProvider>();
builder.Services.AddScoped<IEmailProvider>(sp =>
    sp.GetRequiredService<IBrevoApiClient>().IsConfigured || sp.GetRequiredService<IWebHostEnvironment>().IsProduction()
        ? sp.GetRequiredService<BrevoEmailProvider>()
        : sp.GetRequiredService<LogEmailProvider>());
builder.Services.AddScoped<IReaderTextFactory, ReaderTextFactory>();
builder.Services.AddScoped<IAccountEmailService, AccountEmailService>();
// "Forgot password?" (one-time links in identity.account_tokens).
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IAutomationCleanupService, AutomationCleanupService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
// Settings by section + key: config.settings, else the constant default in Common/Statics/ConfigDefaults.
builder.Services.AddSingleton<IConfigManager, ConfigManager>();
builder.Services.AddHostedService<ConfigRefreshJob>();
// Cache: ICacheManager over ICacheAdapter (in-memory now; register a Redis adapter here instead to share it across
// instances). Keys and lifetimes: Common/Statics/CacheKeys.
builder.Services.AddSingleton<ICacheAdapter, MemoryCacheAdapter>();
builder.Services.AddSingleton<ICacheManager, CacheManager>();
builder.Services.AddScoped<ICacheAdminService, CacheAdminService>();
// Platform admin: the cross-client APIs the admin portal calls (/api/platform-admin/{auth,admins,clients,channels,members,contacts,content,overview}).
builder.Services.AddScoped<IClientAdminService, ClientAdminService>();
builder.Services.AddScoped<IChannelAdminService, ChannelAdminService>();
builder.Services.AddScoped<IMemberAdminService, MemberAdminService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddScoped<IContactAdminService, ContactAdminService>();
builder.Services.AddScoped<IContentAdminService, ContentAdminService>();
builder.Services.AddScoped<IKnowledgeBenchmarkAdminService, KnowledgeBenchmarkAdminService>();
builder.Services.AddScoped<IOverviewAdminService, OverviewAdminService>();
builder.Services.AddScoped<IApiTimingAdminService, ApiTimingAdminService>();
// API timings: RequestTimingMiddleware records every request in memory; ApiTimingFlushJob writes one row per route per
// minute to activity.api_route_stats and trims it (Monitoring:Enabled / SlowRequestMs / RetentionDays).
builder.Services.AddSingleton<IApiTimingCollector, ApiTimingCollector>();
builder.Services.AddScoped<IApiTimingService, ApiTimingService>();
builder.Services.AddHostedService<ApiTimingFlushJob>();
// Error log: DatabaseLoggerProvider (an extra logging provider, so every ILogger also writes here) queues warnings and
// errors in memory; AppLogFlushJob writes them to activity.app_logs and trims it (ErrorLog:* settings).
builder.Services.AddSingleton<IAppLogCollector, AppLogCollector>();
builder.Services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();
builder.Services.AddScoped<IAppLogService, AppLogService>();
builder.Services.AddScoped<IAppLogAdminService, AppLogAdminService>();
builder.Services.AddHostedService<AppLogFlushJob>();
// Message trace: IMessageTracer records each step a message takes (webhook, inbox, bot, send) in memory under the
// request's trace id; MessageTraceFlushJob writes them to activity.message_trace_steps and trims it (MessageTrace:*).
builder.Services.AddSingleton<IMessageTracer, MessageTracer>();
builder.Services.AddScoped<IMessageTraceService, MessageTraceService>();
builder.Services.AddScoped<IMessageTraceAdminService, MessageTraceAdminService>();
builder.Services.AddHostedService<MessageTraceFlushJob>();
builder.Services.AddScoped<IContactService, ContactService>();
// Opt-out (Milestone C): stop/start words on inbound messages and the contact page switch; campaigns, reminders and
// Inbox templates skip or refuse an opted-out contact.
builder.Services.AddScoped<IOptOutService, OptOutService>();
// Privacy requests (Milestone C): export a contact's data as JSON, or delete it for good (account owner or the portal).
builder.Services.AddScoped<IContactDataService, ContactDataService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ICalendarIntegrationService, CalendarIntegrationService>();
builder.Services.AddScoped<IBookingService, BookingService>();
// Booking reminders (Milestone C): settings per client, BookingReminderWorker sends the due ones.
builder.Services.AddScoped<IBookingReminderService, BookingReminderService>();
builder.Services.AddHostedService<BookingReminderWorker>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IChannelIntegrationService, ChannelIntegrationService>();
builder.Services.AddHttpClient<IMetaGraphClient, MetaGraphClient>();

// Calendar Integrations OAuth — Omni's own direct Google/Outlook OAuth2 clients (see
// ICalendarProviderClient's doc comment). Registered as their own concrete types so each keeps its own configured
// HttpClient, then forwarded into the ICalendarProviderClient collection that CalendarIntegrationService and
// CalendarOAuthController resolve by Provider — same multi-implementation pattern as IChannelSender.
builder.Services.AddHttpClient<GoogleCalendarProviderClient>();
builder.Services.AddHttpClient<OutlookCalendarProviderClient>();
builder.Services.AddScoped<ICalendarProviderClient>(sp => sp.GetRequiredService<GoogleCalendarProviderClient>());
builder.Services.AddScoped<ICalendarProviderClient>(sp => sp.GetRequiredService<OutlookCalendarProviderClient>());

// TikTok Login Kit — account connection only, not a messaging channel (see ITikTokIntegrationService's doc
// comment). Same Omni-owns-OAuth-directly shape as Calendar Integrations, no n8n involvement.
builder.Services.AddHttpClient<TikTokProviderClient>();
builder.Services.AddScoped<ITikTokProviderClient>(sp => sp.GetRequiredService<TikTokProviderClient>());
builder.Services.AddScoped<ITikTokIntegrationService, TikTokIntegrationService>();

// Inbox — see Hubs/InboxHub.cs and the Services/I*.cs doc comments for the overall architecture
// (Case A/B/C flows, PostgreSQL-authoritative + SignalR-notifies-only).
builder.Services.AddScoped<IInboxNotifier, InboxNotifier>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();

// The Meta Cloud API behind IWhatsAppService (sends) and IWhatsAppTemplateService (template review).
builder.Services.AddHttpClient<IWhatsAppProvider, MetaWhatsAppProvider>();
builder.Services.AddScoped<IWhatsAppTemplateProvider, MetaWhatsAppTemplateProvider>();
builder.Services.AddSignalR();

// WhatsApp Templates & Campaigns — see Business/Contracts/Services/WhatsApp/IWhatsAppTemplateService.cs and ICampaignService.cs
// for how these build on the Inbox's central MessageService instead of duplicating send logic.
builder.Services.AddScoped<IWhatsAppTemplateService, WhatsAppTemplateService>();
builder.Services.AddScoped<ICampaignAudienceService, CampaignAudienceService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
// Starts scheduled campaigns on time and keeps sending running ones (Campaigns:SenderIntervalSeconds / SendBatchSize).
builder.Services.AddHostedService<CampaignSenderWorker>();
builder.Services.AddScoped<IWhatsAppHealthService, WhatsAppHealthService>();

// Client Knowledge Base — dashboard CRUD, chunking, embeddings (Embeddings:* config) and the semantic
// search behind POST /api/ai/knowledge/search. See Business/Contracts/Services/Knowledge/IKnowledgeService.cs.
builder.Services.AddScoped<IKnowledgeSettingsService, KnowledgeSettingsService>();
builder.Services.AddScoped<IKnowledgeChunkingService, KnowledgeChunkingService>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
builder.Services.AddScoped<IKnowledgeSearchService, KnowledgeSearchService>();

// Knowledge Base WEBSITE SCRAPING — a standalone ingestion subsystem (Integrations/Knowledge/WebScraping). It owns
// crawling/URL identity/fetching/extraction/page state/change detection and hands clean text to IKnowledgeService,
// so pages flow through the SAME chunking/embedding/search as manual entries and uploads.
builder.Services.AddSingleton<SsrfGuard>();
builder.Services.AddSingleton<IHtmlContentExtractor, HtmlContentExtractor>();
builder.Services.AddSingleton<IWebsiteScrapeQueue, WebsiteScrapeQueue>();
builder.Services.AddHttpClient<IWebsiteFetchClient, WebsiteFetchClient>(client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan; // per-request timeouts are enforced by the fetch client
    })
    .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,                                   // redirects are followed manually so each hop is re-validated
        UseCookies = false,
        UseProxy = false,                                            // no proxy: the SSRF guard must see the real destination
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = sp.GetRequiredService<SsrfGuard>().ConnectAsync
    });
builder.Services.AddScoped<IWebsiteScrapeProcessor, WebsiteScrapeProcessor>();
builder.Services.AddScoped<IWebsiteSourceService, WebsiteSourceService>();
builder.Services.AddHostedService<WebsiteScrapeWorker>();

// Knowledge RETRIEVAL BENCHMARK — a standalone diagnostic module (Integrations/Knowledge/Benchmark). It is a CLIENT of
// IKnowledgeSearchService (the production retrieval engine registered above) and of the separate n8n benchmark-question
// workflow; nothing in production ingestion/search depends on it, so it can be removed without touching them.
// RemoveAllLoggers: the n8n webhook URL acts as the credential for that endpoint, so request URIs must not be logged.
builder.Services.AddSingleton<IKnowledgeBenchmarkScorer, KnowledgeBenchmarkScorer>();
builder.Services.AddSingleton<IKnowledgeBenchmarkRunQueue, KnowledgeBenchmarkRunQueue>();
builder.Services.AddHttpClient<IKnowledgeBenchmarkGeneratorClient, N8nKnowledgeBenchmarkGeneratorClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(3); // the workflow answers only after an LLM has written the questions
    })
    .RemoveAllLoggers();
builder.Services.AddScoped<IKnowledgeBenchmarkService, KnowledgeBenchmarkService>();
builder.Services.AddHostedService<KnowledgeBenchmarkRunWorker>();

// Unified raw Meta WhatsApp webhook endpoint — see Business/Engines/WhatsApp/MetaWebhookProcessor.cs.
// The Handlers are thin adapters over the services already registered above; registering them here
// just lets MetaWebhookProcessor receive them via constructor injection like everything else.
builder.Services.AddScoped<CustomerMessageHandler>();
builder.Services.AddScoped<BusinessAppEchoHandler>();
builder.Services.AddScoped<MessageStatusHandler>();
builder.Services.AddScoped<TemplateEventHandler>();
builder.Services.AddScoped<HealthEventHandler>();
builder.Services.AddScoped<HistoryHandler>();
builder.Services.AddScoped<AppStateSyncHandler>();
builder.Services.AddScoped<UnknownEventHandler>();
builder.Services.AddScoped<IMetaWebhookProcessor, MetaWebhookProcessor>();

// Telegram (direct Bot API) — a channel adapter alongside WhatsApp. Inbound: TelegramWebhookController ->
// TelegramWebhookProcessor -> the same Contact/Conversation/Message services. Outbound: IChannelSender
// implementations are resolved by conversation.Channel inside MessageService.
// RemoveAllLoggers: Telegram puts the bot token in the request URL, and the default HttpClient logging
// prints request URIs — so this client must not log at all.
builder.Services.AddHttpClient<ITelegramBotClient, TelegramBotClient>()
    .RemoveAllLoggers();
builder.Services.AddScoped<ITelegramIntegrationService, TelegramIntegrationService>();
builder.Services.AddScoped<ITelegramWebhookProcessor, TelegramWebhookProcessor>();
builder.Services.AddScoped<IChannelSender, WhatsAppChannelSender>();
builder.Services.AddScoped<IChannelSender, TelegramChannelSender>();
// Voice notes: one media source per channel (resolved by Message.Channel, like IChannelSender) + the Inbox playback service.
builder.Services.AddScoped<IVoiceMediaSource, TelegramVoiceMediaSource>();
builder.Services.AddScoped<IMessageMediaService, MessageMediaService>();

// Subscriptions & usage billing — an internal module (Billing/, docs/billing.md): plans + entitlements, prepaid
// wallet + included credit, rate cards, usage records, the ledger, and one maintenance worker. Off until
// Billing:Enabled = true.
Omni.App.Common.Extensions.BillingModule.AddBilling(builder.Services, builder.Configuration);

// Outbound: the one call to n8n left after Meta started posting directly to us — see
// Controllers/Integrations/WhatsAppWebhookController.cs and IAiTriggerNotifier's own doc comment.
builder.Services.AddHttpClient<IAiTriggerNotifier, AiTriggerNotifier>();
builder.Services.AddHttpClient<ICalendarSyncNotifier, CalendarSyncNotifier>();

// ---------------------------------------------------------------------
// Web layer
// ---------------------------------------------------------------------
// Every Razor Page requires login by default — the sign-in pages (Login, Register, Forgot/Reset password,
// AcceptInvite) and the framework's own Error page are the only ones explicitly opened up, since a user obviously
// can't log in on a page that itself requires being logged in.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Register");
    options.Conventions.AllowAnonymousToPage("/Account/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/Account/ResetPassword");
    options.Conventions.AllowAnonymousToPage("/Account/AcceptInvite");
    // Authenticated by the short-lived Google external cookie, not yet by an app session (see GoogleAuthController).
    options.Conventions.AllowAnonymousToPage("/Account/CompleteGoogleSignup");
    options.Conventions.AllowAnonymousToPage("/Error");
    // The friendly 404/400 page (UseStatusCodePagesWithReExecute below) must render for signed-out visitors too.
    options.Conventions.AllowAnonymousToPage("/Status");
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Omni API",
        Version = "v1",
        Description = "Contact intake, qualification, booking and dashboard API for clients on the platform. " +
                      "Dashboard endpoints require a logged-in session and resolve clientId server-side " +
                      "via CurrentClientContext; the n8n-facing endpoints (ingest, AI tools, WhatsApp " +
                      "webhook) use a separate shared-secret header instead — see RequireIngestKeyAttribute."
    });
});

var app = builder.Build();

// `dotnet run -- create-admin <email> "<full name>"` creates an admin user (role admin; password from ADMIN_PASSWORD or
// typed in) and exits. The only way to make the first admin for the admin portal; later ones come from its Admins page.
if (args.Length > 0 && args[0] == AdminCli.CreateAdminCommand)
{
    await AdminCli.CreateAdminAsync(app.Services, args);
    return;
}

app.UseForwardedHeaders();

// ---------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Omni API v1");
    });
}

// Pages with an error status and no body (404, 400…) show the friendly Status page instead of the browser's blank one.
// Not for the JSON APIs, the SignalR hubs or static files: those keep their plain status.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api")
                   && !ctx.Request.Path.StartsWithSegments("/hubs")
                   && !Path.HasExtension(ctx.Request.Path.Value),
    branch => branch.UseStatusCodePagesWithReExecute("/Status", "?code={0}"));

app.UseHttpsRedirection();

app.UseRouting();

// After routing (the matched route template names the timing), before everything that can be slow or fail.
app.UseMiddleware<RequestTimingMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// After authentication: the language and business-type words depend on the signed-in user's client.
app.UseMiddleware<RequestTextMiddleware>();

app.MapStaticAssets();
app.MapGet("/", () => Results.Redirect("/dashboard"));
app.MapRazorPages().WithStaticAssets();
app.MapControllers();
app.MapHub<InboxHub>("/hubs/inbox");

app.Run();
