using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Hubs;
using PlasticSurgery.Integrations.WhatsApp;
using PlasticSurgery.Integrations.WhatsApp.Handlers;
using PlasticSurgery.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------
// Authentication — ASP.NET Core Identity, authentication only. No roles, no permissions matrix:
// this MVP deliberately ignores owner/manager/receptionist/surgeon distinctions (see ClinicUser's
// doc comment) — every logged-in user linked to a clinic has full access to that clinic's data.
// AddIdentityCore (not the full AddIdentity<TUser,TRole>) keeps roles out entirely; cookie auth is
// wired up explicitly below rather than via AddIdentity's built-in scheme registration, for the
// same reason. See Services/ICurrentClinicContext.cs for how a logged-in user resolves to a clinic.
// ---------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();

builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireNonAlphanumeric = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
    })
    // Holds the Google identity for the few minutes between "Google said who this is" and "we signed them in
    // (or collected their clinic name first)" — AddIdentityCore doesn't register this scheme itself.
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------
// Application services — shared by both the API controllers and the
// Razor Pages dashboard, so business logic and clinic-scoping live in
// exactly one place.
// ---------------------------------------------------------------------
builder.Services.AddScoped<IEventLogger, EventLogger>();
builder.Services.AddScoped<IClinicContext, ClinicContext>();
builder.Services.AddScoped<ICurrentClinicContext, CurrentClinicContext>();
builder.Services.AddScoped<IClinicRegistrationService, ClinicRegistrationService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IProcedureService, ProcedureService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ICalendarIntegrationService, CalendarIntegrationService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IProcedureBookingService, ProcedureBookingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IChannelIntegrationService, ChannelIntegrationService>();
builder.Services.AddHttpClient<IMetaGraphClient, MetaGraphClient>();

// Calendar Integrations OAuth — SculptFlow's own direct Google/Outlook OAuth2 clients (see
// ICalendarProviderClient's doc comment). Registered as their own concrete types so each keeps its own configured
// HttpClient, then forwarded into the ICalendarProviderClient collection that CalendarIntegrationService and
// CalendarOAuthController resolve by Provider — same multi-implementation pattern as IChannelSender.
builder.Services.AddHttpClient<GoogleCalendarProviderClient>();
builder.Services.AddHttpClient<OutlookCalendarProviderClient>();
builder.Services.AddScoped<ICalendarProviderClient>(sp => sp.GetRequiredService<GoogleCalendarProviderClient>());
builder.Services.AddScoped<ICalendarProviderClient>(sp => sp.GetRequiredService<OutlookCalendarProviderClient>());

// TikTok Login Kit — account connection only, not a messaging channel (see ITikTokIntegrationService's doc
// comment). Same SculptFlow-owns-OAuth-directly shape as Calendar Integrations, no n8n involvement.
builder.Services.AddHttpClient<TikTokProviderClient>();
builder.Services.AddScoped<ITikTokProviderClient>(sp => sp.GetRequiredService<TikTokProviderClient>());
builder.Services.AddScoped<ITikTokIntegrationService, TikTokIntegrationService>();

// Inbox — see Hubs/InboxHub.cs and the Services/I*.cs doc comments for the overall architecture
// (Case A/B/C flows, PostgreSQL-authoritative + SignalR-notifies-only).
builder.Services.AddScoped<IInboxNotifier, InboxNotifier>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();

// WhatsApp providers (BSPs) behind IWhatsAppService — the global WhatsApp:Provider setting picks one
// (see Services/IWhatsAppProvider.cs). Same concrete-type-then-forward pattern as ICalendarProviderClient.
builder.Services.AddHttpClient<PlasticSurgery.Integrations.WhatsApp.MetaWhatsAppProvider>();
builder.Services.AddScoped<IWhatsAppProvider>(sp => sp.GetRequiredService<PlasticSurgery.Integrations.WhatsApp.MetaWhatsAppProvider>());
// Infobip: SculptFlow's own account (Infobip:BaseUrl / Infobip:ApiKey env vars). The key travels only in the
// Authorization header, which HttpClient logging never prints; the client itself logs no bodies or numbers.
builder.Services.AddHttpClient<PlasticSurgery.Integrations.Infobip.IInfobipClient, PlasticSurgery.Integrations.Infobip.InfobipClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(builder.Configuration.GetValue("Infobip:TimeoutSeconds", 20), 5, 120)));
builder.Services.AddScoped<IWhatsAppProvider, PlasticSurgery.Integrations.Infobip.InfobipWhatsAppProvider>();
// Template review for the same providers, picked by the same WhatsApp:Provider switch (see IWhatsAppTemplateProvider).
builder.Services.AddScoped<IWhatsAppTemplateProvider, PlasticSurgery.Integrations.WhatsApp.MetaWhatsAppTemplateProvider>();
builder.Services.AddScoped<IWhatsAppTemplateProvider, PlasticSurgery.Integrations.Infobip.InfobipWhatsAppTemplateProvider>();
builder.Services.AddScoped<PlasticSurgery.Integrations.Infobip.IInfobipWhatsAppIntegrationService, PlasticSurgery.Integrations.Infobip.InfobipWhatsAppIntegrationService>();
builder.Services.AddScoped<PlasticSurgery.Integrations.Infobip.IInfobipWhatsAppWebhookProcessor, PlasticSurgery.Integrations.Infobip.InfobipWhatsAppWebhookProcessor>();
builder.Services.AddSignalR();

// WhatsApp Templates & Campaigns — see Services/IWhatsAppTemplateService.cs and ICampaignService.cs
// for how these build on the Inbox's central MessageService instead of duplicating send logic.
builder.Services.AddScoped<IWhatsAppTemplateService, WhatsAppTemplateService>();
builder.Services.AddScoped<ICampaignAudienceService, CampaignAudienceService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<IWhatsAppHealthService, WhatsAppHealthService>();

// Clinic Knowledge Base — dashboard CRUD, chunking, embeddings (Embeddings:* config) and the semantic
// search behind POST /api/ai/knowledge/search. See Services/IKnowledgeService.cs.
builder.Services.AddScoped<IKnowledgeSettingsService, KnowledgeSettingsService>();
builder.Services.AddScoped<IKnowledgeChunkingService, KnowledgeChunkingService>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
builder.Services.AddScoped<IKnowledgeSearchService, KnowledgeSearchService>();

// Knowledge Base WEBSITE SCRAPING — a standalone ingestion subsystem (Integrations/Knowledge/WebScraping). It owns
// crawling/URL identity/fetching/extraction/page state/change detection and hands clean text to IKnowledgeService,
// so pages flow through the SAME chunking/embedding/search as manual entries and uploads.
builder.Services.AddSingleton(sp => PlasticSurgery.Integrations.Knowledge.WebScraping.WebsiteScrapeOptions.Resolve(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<PlasticSurgery.Integrations.Knowledge.WebScraping.SsrfGuard>();
builder.Services.AddSingleton<PlasticSurgery.Integrations.Knowledge.WebScraping.IHtmlContentExtractor, PlasticSurgery.Integrations.Knowledge.WebScraping.HtmlContentExtractor>();
builder.Services.AddSingleton<PlasticSurgery.Integrations.Knowledge.WebScraping.IWebsiteScrapeQueue, PlasticSurgery.Integrations.Knowledge.WebScraping.WebsiteScrapeQueue>();
builder.Services.AddHttpClient<PlasticSurgery.Integrations.Knowledge.WebScraping.IWebsiteFetchClient, PlasticSurgery.Integrations.Knowledge.WebScraping.WebsiteFetchClient>(client =>
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
        ConnectCallback = sp.GetRequiredService<PlasticSurgery.Integrations.Knowledge.WebScraping.SsrfGuard>().ConnectAsync
    });
builder.Services.AddScoped<PlasticSurgery.Integrations.Knowledge.WebScraping.IWebsiteScrapeProcessor, PlasticSurgery.Integrations.Knowledge.WebScraping.WebsiteScrapeProcessor>();
builder.Services.AddScoped<PlasticSurgery.Integrations.Knowledge.WebScraping.IWebsiteSourceService, PlasticSurgery.Integrations.Knowledge.WebScraping.WebsiteSourceService>();
builder.Services.AddHostedService<PlasticSurgery.Integrations.Knowledge.WebScraping.WebsiteScrapeWorker>();

// Knowledge RETRIEVAL BENCHMARK — a standalone diagnostic module (Integrations/Knowledge/Benchmark). It is a CLIENT of
// IKnowledgeSearchService (the production retrieval engine registered above) and of the separate n8n benchmark-question
// workflow; nothing in production ingestion/search depends on it, so it can be removed without touching them.
// RemoveAllLoggers: the n8n webhook URL acts as the credential for that endpoint, so request URIs must not be logged.
builder.Services.AddSingleton<PlasticSurgery.Integrations.Knowledge.Benchmark.IKnowledgeBenchmarkScorer, PlasticSurgery.Integrations.Knowledge.Benchmark.KnowledgeBenchmarkScorer>();
builder.Services.AddSingleton<PlasticSurgery.Integrations.Knowledge.Benchmark.IKnowledgeBenchmarkRunQueue, PlasticSurgery.Integrations.Knowledge.Benchmark.KnowledgeBenchmarkRunQueue>();
builder.Services.AddHttpClient<PlasticSurgery.Integrations.Knowledge.Benchmark.IKnowledgeBenchmarkGeneratorClient, PlasticSurgery.Integrations.Knowledge.Benchmark.N8nKnowledgeBenchmarkGeneratorClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(3); // the workflow answers only after an LLM has written the questions
    })
    .RemoveAllLoggers();
builder.Services.AddScoped<PlasticSurgery.Integrations.Knowledge.Benchmark.IKnowledgeBenchmarkService, PlasticSurgery.Integrations.Knowledge.Benchmark.KnowledgeBenchmarkService>();
builder.Services.AddHostedService<PlasticSurgery.Integrations.Knowledge.Benchmark.KnowledgeBenchmarkRunWorker>();

// Unified raw Meta WhatsApp webhook endpoint — see Integrations/WhatsApp/MetaWebhookProcessor.cs.
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
// TelegramWebhookProcessor -> the same Lead/Conversation/Message services. Outbound: IChannelSender
// implementations are resolved by conversation.Channel inside MessageService.
// RemoveAllLoggers: Telegram puts the bot token in the request URL, and the default HttpClient logging
// prints request URIs — so this client must not log at all.
builder.Services.AddHttpClient<PlasticSurgery.Integrations.Telegram.ITelegramBotClient, PlasticSurgery.Integrations.Telegram.TelegramBotClient>()
    .RemoveAllLoggers();
builder.Services.AddScoped<PlasticSurgery.Integrations.Telegram.ITelegramIntegrationService, PlasticSurgery.Integrations.Telegram.TelegramIntegrationService>();
builder.Services.AddScoped<PlasticSurgery.Integrations.Telegram.ITelegramWebhookProcessor, PlasticSurgery.Integrations.Telegram.TelegramWebhookProcessor>();
builder.Services.AddScoped<IChannelSender, WhatsAppChannelSender>();
builder.Services.AddScoped<IChannelSender, PlasticSurgery.Integrations.Telegram.TelegramChannelSender>();

// Subscriptions & usage billing — an internal module (Billing/, docs/billing.md): plans + entitlements, prepaid
// wallet + included credit, rate cards, usage records, the ledger, and one maintenance worker. Off until
// Billing:Enabled = true.
PlasticSurgery.Billing.BillingModule.AddBilling(builder.Services, builder.Configuration);

// Outbound: the one call to n8n left after Meta started posting directly to us — see
// Controllers/WhatsAppWebhookController.cs and IAiTriggerNotifier's own doc comment.
builder.Services.AddHttpClient<IAiTriggerNotifier, AiTriggerNotifier>();
builder.Services.AddHttpClient<ICalendarSyncNotifier, CalendarSyncNotifier>();

// ---------------------------------------------------------------------
// Web layer
// ---------------------------------------------------------------------
// Every Razor Page requires login by default — Account/Login and Account/Register (and the
// framework's own Error page) are the only ones explicitly opened up, since a user obviously can't
// log in on a page that itself requires being logged in.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Register");
    // Authenticated by the short-lived Google external cookie, not yet by an app session (see GoogleAuthController).
    options.Conventions.AllowAnonymousToPage("/Account/CompleteGoogleSignup");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Plastic Surgery Clinic API",
        Version = "v1",
        Description = "Lead intake, qualification, booking and dashboard API for clinics on the platform. " +
                      "Dashboard endpoints require a logged-in session and resolve clinicId server-side " +
                      "via CurrentClinicContext; the n8n-facing endpoints (ingest, AI tools, WhatsApp " +
                      "webhook) use a separate shared-secret header instead — see RequireIngestKeyAttribute."
    });
});

var app = builder.Build();

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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Plastic Surgery Clinic API v1");
    });
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGet("/", () => Results.Redirect("/dashboard"));
app.MapRazorPages().WithStaticAssets();
app.MapControllers();
app.MapHub<InboxHub>("/hubs/inbox");

app.Run();
