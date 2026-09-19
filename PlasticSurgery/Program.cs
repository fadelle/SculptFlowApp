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

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
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
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IProcedureService, ProcedureService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IProcedureBookingService, ProcedureBookingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IChannelIntegrationService, ChannelIntegrationService>();
builder.Services.AddHttpClient<IMetaGraphClient, MetaGraphClient>();

// Inbox — see Hubs/InboxHub.cs and the Services/I*.cs doc comments for the overall architecture
// (Case A/B/C flows, PostgreSQL-authoritative + SignalR-notifies-only).
builder.Services.AddScoped<IInboxNotifier, InboxNotifier>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>();
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

// Outbound: the one call to n8n left after Meta started posting directly to us — see
// Controllers/WhatsAppWebhookController.cs and IAiTriggerNotifier's own doc comment.
builder.Services.AddHttpClient<IAiTriggerNotifier, AiTriggerNotifier>();

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
