using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contexts;

/// <summary>
/// EF Core context mapped onto the schema owned by Database/schema.sql.
/// This context does NOT own migrations — the schema is created/altered
/// by running schema.sql directly (psql or the Supabase SQL editor), per
/// the plan's "use SQL migrations" principle. Every mapping below must
/// stay in sync with schema.sql by hand.
///
/// Inherits IdentityUserContext&lt;AppUser&gt; (not the full IdentityDbContext, which also adds
/// AspNetRoles/AspNetUserRoles/AspNetRoleClaims): a user's role is the users.role column (UserRole), so there
/// are no role tables. Identity's own tables are remapped to identity.users / user_claims / user_logins /
/// user_tokens in snake_case below rather than EF's default PascalCase "AspNetUsers" etc.
/// </summary>
public class ApplicationDbContext : IdentityUserContext<AppUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<EventLog> Events => Set<EventLog>();
    public DbSet<ChannelIntegration> ChannelIntegrations => Set<ChannelIntegration>();
    public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<WhatsAppHealthEvent> WhatsAppHealthEvents => Set<WhatsAppHealthEvent>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<KnowledgeSearchSettings> KnowledgeSearchSettings => Set<KnowledgeSearchSettings>();
    public DbSet<KnowledgeWebsiteSource> KnowledgeWebsiteSources => Set<KnowledgeWebsiteSource>();
    public DbSet<KnowledgeWebsitePage> KnowledgeWebsitePages => Set<KnowledgeWebsitePage>();
    public DbSet<KnowledgeWebsiteScrapeRun> KnowledgeWebsiteScrapeRuns => Set<KnowledgeWebsiteScrapeRun>();
    public DbSet<KnowledgeRetrievalBenchmarkCase> KnowledgeBenchmarkCases => Set<KnowledgeRetrievalBenchmarkCase>();
    public DbSet<KnowledgeRetrievalBenchmarkRun> KnowledgeBenchmarkRuns => Set<KnowledgeRetrievalBenchmarkRun>();
    public DbSet<KnowledgeRetrievalBenchmarkResult> KnowledgeBenchmarkResults => Set<KnowledgeRetrievalBenchmarkResult>();
    public DbSet<KnowledgeRetrievalBenchmarkGeneration> KnowledgeBenchmarkGenerations => Set<KnowledgeRetrievalBenchmarkGeneration>();
    public DbSet<ClientUser> ClientUsers => Set<ClientUser>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    public DbSet<ReminderSettings> ReminderSettings => Set<ReminderSettings>();
    public DbSet<BookingReminder> BookingReminders => Set<BookingReminder>();
    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();
    public DbSet<BookingSettings> BookingSettings => Set<BookingSettings>();
    public DbSet<InboxSettings> InboxSettings => Set<InboxSettings>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CalendarIntegration> CalendarIntegrations => Set<CalendarIntegration>();
    public DbSet<CalendarIntegrationCalendar> CalendarIntegrationCalendars => Set<CalendarIntegrationCalendar>();
    public DbSet<BookingCalendarSync> BookingCalendarSyncs => Set<BookingCalendarSync>();
    public DbSet<TikTokIntegration> TikTokIntegrations => Set<TikTokIntegration>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<SubscriptionPlanEntitlement> SubscriptionPlanEntitlements => Set<SubscriptionPlanEntitlement>();
    public DbSet<ClientSubscription> ClientSubscriptions => Set<ClientSubscription>();
    public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();
    public DbSet<BillingRateCard> BillingRateCards => Set<BillingRateCard>();
    public DbSet<BillingRate> BillingRates => Set<BillingRate>();
    public DbSet<BillingUsageRecord> BillingUsageRecords => Set<BillingUsageRecord>();
    public DbSet<BillingLedgerEntry> BillingLedgerEntries => Set<BillingLedgerEntry>();
    public DbSet<ChannelAccountBillingSettings> ChannelAccountBillingSettings => Set<ChannelAccountBillingSettings>();
    public DbSet<ConfigSetting> ConfigSettings => Set<ConfigSetting>();
    public DbSet<ApiRouteStat> ApiRouteStats => Set<ApiRouteStat>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();
    public DbSet<MessageTraceStep> MessageTraceSteps => Set<MessageTraceStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Lets IdentityUserContext configure its own entities first (AppUser, IdentityUserClaim,
        // IdentityUserLogin, IdentityUserToken) before we remap their table/column names below.
        base.OnModelCreating(modelBuilder);

        // ---------------------------------------------------------------
        // Identity tables — remapped from EF's default PascalCase ("AspNetUsers" etc.) to this
        // schema's snake_case convention. See Database/schema.sql for the matching DDL.
        // ---------------------------------------------------------------
        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("users", "identity");
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
            e.Property(u => u.FullName).HasColumnName("full_name");
            e.Property(u => u.Language).HasColumnName("language").HasMaxLength(5);
            e.Property(u => u.IsActive).HasColumnName("is_active");
            e.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
            e.Property(u => u.CreatedAt).HasColumnName("created_at");
            e.Property(u => u.UpdatedAt).HasColumnName("updated_at");
            e.Property(u => u.UserName).HasColumnName("user_name").HasMaxLength(256);
            e.Property(u => u.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(256);
            e.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            e.Property(u => u.EmailConfirmed).HasColumnName("email_confirmed");
            e.Property(u => u.PasswordHash).HasColumnName("password_hash");
            e.Property(u => u.SecurityStamp).HasColumnName("security_stamp");
            e.Property(u => u.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            e.Property(u => u.PhoneNumber).HasColumnName("phone_number");
            e.Property(u => u.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            e.Property(u => u.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            e.Property(u => u.LockoutEnd).HasColumnName("lockout_end");
            e.Property(u => u.LockoutEnabled).HasColumnName("lockout_enabled");
            e.Property(u => u.AccessFailedCount).HasColumnName("access_failed_count");
        });

        modelBuilder.Entity<IdentityUserClaim<string>>(e =>
        {
            e.ToTable("user_claims", "identity");
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.UserId).HasColumnName("user_id");
            e.Property(c => c.ClaimType).HasColumnName("claim_type");
            e.Property(c => c.ClaimValue).HasColumnName("claim_value");
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(e =>
        {
            e.ToTable("user_logins", "identity");
            e.Property(l => l.LoginProvider).HasColumnName("login_provider");
            e.Property(l => l.ProviderKey).HasColumnName("provider_key");
            e.Property(l => l.ProviderDisplayName).HasColumnName("provider_display_name");
            e.Property(l => l.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<IdentityUserToken<string>>(e =>
        {
            e.ToTable("user_tokens", "identity");
            e.Property(t => t.UserId).HasColumnName("user_id");
            e.Property(t => t.LoginProvider).HasColumnName("login_provider");
            e.Property(t => t.Name).HasColumnName("name");
            e.Property(t => t.Value).HasColumnName("value");
        });

        // ---------------------------------------------------------------
        // clients
        // ---------------------------------------------------------------
        modelBuilder.Entity<Client>(e =>
        {
            e.ToTable("clients", "core");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(50);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
            e.Property(x => x.Website).HasColumnName("website").HasMaxLength(300);
            e.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(10);
            e.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(100).IsRequired();
            e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.OperatingHours).HasColumnName("operating_hours");
            e.Property(x => x.BookingInfo).HasColumnName("booking_info");
            e.Property(x => x.BusinessTypeCode).HasColumnName("business_type_code").HasMaxLength(50).IsRequired();
            e.Property(x => x.DefaultLanguage).HasColumnName("default_language").HasMaxLength(10).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasOne<BusinessType>().WithMany().HasForeignKey(x => x.BusinessTypeCode).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------------------------------------------------------------
        // business types and their pipeline stages
        // ---------------------------------------------------------------
        modelBuilder.Entity<BusinessType>(e =>
        {
            e.ToTable("business_types", "core");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.NameJson).HasColumnName("name").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.TerminologyJson).HasColumnName("terminology").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.AiProfile).HasColumnName("ai_profile").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasMany(x => x.Stages).WithOne().HasForeignKey(x => x.BusinessTypeCode).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineStage>(e =>
        {
            e.ToTable("pipeline_stages", "crm");
            e.HasKey(x => new { x.BusinessTypeCode, x.Key });
            e.Property(x => x.BusinessTypeCode).HasColumnName("business_type_code").HasMaxLength(50);
            e.Property(x => x.Key).HasColumnName("key").HasMaxLength(50);
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
            e.Property(x => x.LabelsJson).HasColumnName("labels").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // ---------------------------------------------------------------
        // services
        // ---------------------------------------------------------------
        modelBuilder.Entity<Service>(e =>
        {
            e.ToTable("services", "core");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(100);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.BookingDuration).HasColumnName("booking_duration");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany(c => c.Services)
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
        });

        // ---------------------------------------------------------------
        // contacts
        // ---------------------------------------------------------------
        modelBuilder.Entity<Contact>(e =>
        {
            e.ToTable("contacts", "crm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200);
            e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100);
            e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(50);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(100);
            e.Property(x => x.SourceDetail).HasColumnName("source_detail").HasMaxLength(200);
            e.Property(x => x.CampaignName).HasColumnName("campaign_name").HasMaxLength(200);
            e.Property(x => x.ExternalContactId).HasColumnName("external_contact_id").HasMaxLength(200);
            e.Property(x => x.StageKey).HasColumnName("stage_key").HasMaxLength(50).IsRequired();
            e.Property(x => x.Qualification).HasColumnName("qualification").HasMaxLength(50).IsRequired();
            e.Property(x => x.PreferredLanguage).HasColumnName("preferred_language").HasMaxLength(20);
            e.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(10);
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
            e.Property(x => x.DesiredTimeline).HasColumnName("desired_timeline").HasMaxLength(100);
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.AssignedUserId).HasColumnName("assigned_user_id");
            e.Property(x => x.MarketingOptIn).HasColumnName("marketing_opt_in");
            e.Property(x => x.OptedOutAt).HasColumnName("opted_out_at");
            e.Property(x => x.LastContactAt).HasColumnName("last_contact_at");
            e.Property(x => x.NextFollowupAt).HasColumnName("next_followup_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany(c => c.Contacts)
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Service).WithMany()
                .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => new { x.ClientId, x.Phone });
            e.HasIndex(x => new { x.ClientId, x.Email });
            e.HasIndex(x => new { x.ClientId, x.StageKey });
            e.HasIndex(x => x.ServiceId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.ClientId, x.ExternalContactId })
                .IsUnique()
                .HasFilter("external_contact_id IS NOT NULL")
                .HasDatabaseName("ux_contacts_client_external_contact_id");
        });

        // ---------------------------------------------------------------
        // conversations
        // ---------------------------------------------------------------
        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations", "crm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(50).IsRequired();
            e.Property(x => x.ExternalThreadId).HasColumnName("external_thread_id").HasMaxLength(200);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            e.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(30).IsRequired();
            e.Property(x => x.AiEnabled).HasColumnName("ai_enabled");
            e.Property(x => x.HumanTakeover).HasColumnName("human_takeover");
            e.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
            e.Property(x => x.LastMessageDirection).HasColumnName("last_message_direction").HasMaxLength(20);
            e.Property(x => x.LastCustomerMessageAt).HasColumnName("last_customer_message_at");
            e.Property(x => x.ServiceWindowExpiresAt).HasColumnName("service_window_expires_at");
            e.Property(x => x.LastReadAt).HasColumnName("last_read_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Contact).WithMany(l => l.Conversations)
                .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.ServiceWindowExpiresAt);
        });

        // ---------------------------------------------------------------
        // messages
        // ---------------------------------------------------------------
        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("messages", "crm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(20).IsRequired();
            e.Property(x => x.SenderType).HasColumnName("sender_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(50).IsRequired();
            e.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(30).IsRequired();
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(200);
            e.Property(x => x.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(30);
            e.Property(x => x.IsAiGenerated).HasColumnName("is_ai_generated");
            e.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(50).IsRequired();
            e.Property(x => x.WhatsAppTemplateId).HasColumnName("whatsapp_template_id");
            e.Property(x => x.CampaignId).HasColumnName("campaign_id");
            e.Property(x => x.CampaignRecipientId).HasColumnName("campaign_recipient_id");
            e.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            e.Property(x => x.ReadAt).HasColumnName("read_at");
            e.Property(x => x.FailedAt).HasColumnName("failed_at");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            e.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
            e.Property(x => x.FailureReason).HasColumnName("failure_reason");
            e.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            e.Property(x => x.SentAt).HasColumnName("sent_at");
            e.Property(x => x.ReceivedAt).HasColumnName("received_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(32);

            e.HasOne(x => x.Conversation).WithMany(c => c.Messages)
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Contact).WithMany()
                .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.WhatsAppTemplateId);
            // Idempotency for webhook/n8n retries — see MessageService.IngestAsync.
            e.HasIndex(x => new { x.ClientId, x.Channel, x.ExternalMessageId })
                .IsUnique()
                .HasFilter("external_message_id IS NOT NULL")
                .HasDatabaseName("ux_messages_client_channel_external_message_id");
        });

        // ---------------------------------------------------------------
        // bookings
        // ---------------------------------------------------------------
        modelBuilder.Entity<Booking>(e =>
        {
            e.ToTable("bookings", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.BookingType).HasColumnName("booking_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            e.Property(x => x.ScheduledStart).HasColumnName("scheduled_start").IsRequired();
            e.Property(x => x.ScheduledEnd).HasColumnName("scheduled_end");
            e.Property(x => x.LocationType).HasColumnName("location_type").HasMaxLength(30);
            e.Property(x => x.LocationName).HasColumnName("location_name").HasMaxLength(200);
            e.Property(x => x.ExternalCalendarId).HasColumnName("external_calendar_id").HasMaxLength(200);
            e.Property(x => x.ExternalEventId).HasColumnName("external_event_id").HasMaxLength(200);
            e.Property(x => x.AssignedUserId).HasColumnName("assigned_user_id");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Contact).WithMany(l => l.Bookings)
                .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Service).WithMany()
                .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.ScheduledStart);
            e.HasIndex(x => x.Status);
        });

        // ---------------------------------------------------------------
        // orders
        // ---------------------------------------------------------------
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders", "crm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            e.Property(x => x.QuotedAmount).HasColumnName("quoted_amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.FinalAmount).HasColumnName("final_amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
            e.Property(x => x.ServiceDate).HasColumnName("service_date");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Contact).WithMany(l => l.Orders)
                .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Service).WithMany()
                .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany(a => a.Orders)
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.Status);
        });

        // ---------------------------------------------------------------
        // events
        // ---------------------------------------------------------------
        modelBuilder.Entity<EventLog>(e =>
        {
            e.ToTable("events", "activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(50);
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            // These weren't configured before — EventLog has no CLR nav properties for them, but EF
            // still needs to know the FK relationships exist (they do, in schema.sql) so it orders
            // inserts correctly: a brand-new Contact/Conversation/Booking added in the same
            // SaveChangesAsync call as an EventLog row referencing it must insert first, and without
            // this EF has no dependency info to guarantee that ordering (see ContactService.CreateOrGetAsync
            // / GetOrCreateByPhoneAsync, both of which add a new Contact and log its creation event in
            // one call).
            e.HasOne<Client>().WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Contact>().WithMany()
                .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Conversation>().WithMany()
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Booking>().WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.CreatedAt);
        });

        // ---------------------------------------------------------------
        // channel_integrations
        // ---------------------------------------------------------------
        modelBuilder.Entity<ChannelIntegration>(e =>
        {
            e.ToTable("channel_integrations", "channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            e.Property(x => x.PhoneNumberId).HasColumnName("phone_number_id").HasMaxLength(200);
            e.Property(x => x.WhatsAppBusinessId).HasColumnName("whatsapp_business_id").HasMaxLength(200);
            e.Property(x => x.PageId).HasColumnName("page_id").HasMaxLength(200);
            e.Property(x => x.InstagramBusinessId).HasColumnName("instagram_business_id").HasMaxLength(200);
            e.Property(x => x.AccessToken).HasColumnName("access_token");
            e.Property(x => x.WebhookVerifyToken).HasColumnName("webhook_verify_token").HasMaxLength(200);
            e.Property(x => x.Pin).HasColumnName("pin").HasMaxLength(10);
            e.Property(x => x.MetaBusinessId).HasColumnName("meta_business_id").HasMaxLength(200);
            e.Property(x => x.VerifiedName).HasColumnName("verified_name").HasMaxLength(200);
            e.Property(x => x.AccountStatus).HasColumnName("account_status").HasMaxLength(50);
            e.Property(x => x.AccountReviewStatus).HasColumnName("account_review_status").HasMaxLength(50);
            e.Property(x => x.PhoneQualityRating).HasColumnName("phone_quality_rating").HasMaxLength(50);
            e.Property(x => x.PhoneStatus).HasColumnName("phone_status").HasMaxLength(50);
            e.Property(x => x.NameStatus).HasColumnName("name_status").HasMaxLength(50);
            e.Property(x => x.IsHealthy).HasColumnName("is_healthy");
            e.Property(x => x.HealthLevel).HasColumnName("health_level").HasMaxLength(30);
            e.Property(x => x.LastProblemCode).HasColumnName("last_problem_code").HasMaxLength(100);
            e.Property(x => x.LastProblemMessage).HasColumnName("last_problem_message");
            e.Property(x => x.LastWebhookAt).HasColumnName("last_webhook_at");
            e.Property(x => x.LastHealthEventAt).HasColumnName("last_health_event_at");
            e.Property(x => x.TelegramBotId).HasColumnName("telegram_bot_id").HasMaxLength(50);
            e.Property(x => x.TelegramBotUsername).HasColumnName("telegram_bot_username").HasMaxLength(100);
            e.Property(x => x.WebhookStatus).HasColumnName("webhook_status").HasMaxLength(30);
            e.Property(x => x.WebhookRegisteredAt).HasColumnName("webhook_registered_at");
            e.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ClientId, x.Channel })
                .IsUnique()
                .HasDatabaseName("ux_channel_integrations_client_channel");
            // A Meta phone_number_id must never map to more than one client — webhook routing
            // (phone_number_id -> channel_integration -> client_id) depends on this being
            // unambiguous. Filtered (not null only): most rows here are non-WhatsApp channels
            // (Instagram/Facebook) or WhatsApp rows not yet connected, neither of which sets this.
            e.HasIndex(x => x.PhoneNumberId)
                .IsUnique()
                .HasFilter("phone_number_id IS NOT NULL")
                .HasDatabaseName("ux_channel_integrations_phone_number_id");
            // WABA id is deliberately NOT unique — one WABA can contain multiple phone numbers, so
            // multiple channel_integrations rows (even across different clients, in principle) can
            // legitimately share a whatsapp_business_id. Indexed for lookup speed only.
            e.HasIndex(x => x.WhatsAppBusinessId);
        });

        // ---------------------------------------------------------------
        // whatsapp_templates
        // ---------------------------------------------------------------
        modelBuilder.Entity<WhatsAppTemplate>(e =>
        {
            e.ToTable("whatsapp_templates", "channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.MetaTemplateId).HasColumnName("meta_template_id").HasMaxLength(200);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
            e.Property(x => x.Language).HasColumnName("language").HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.HeaderType).HasColumnName("header_type").HasMaxLength(20);
            e.Property(x => x.HeaderContent).HasColumnName("header_content");
            e.Property(x => x.Body).HasColumnName("body").IsRequired();
            e.Property(x => x.Footer).HasColumnName("footer");
            e.Property(x => x.ButtonsJson).HasColumnName("buttons_json").HasColumnType("jsonb");
            e.Property(x => x.VariablesJson).HasColumnName("variables_json").HasColumnType("jsonb");
            e.Property(x => x.RejectionReason).HasColumnName("rejection_reason");
            e.Property(x => x.ChannelIntegrationId).HasColumnName("channel_integration_id");
            e.Property(x => x.QualityRating).HasColumnName("quality_rating").HasMaxLength(30);
            e.Property(x => x.PreviousCategory).HasColumnName("previous_category").HasMaxLength(30);
            e.Property(x => x.CurrentCategory).HasColumnName("current_category").HasMaxLength(30);
            e.Property(x => x.ComponentsJson).HasColumnName("components").HasColumnType("jsonb");
            e.Property(x => x.LastMetaEventAt).HasColumnName("last_meta_event_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ChannelIntegrationId);
            e.HasIndex(x => new { x.ClientId, x.MetaTemplateId })
                .HasDatabaseName("ix_whatsapp_templates_client_meta_template_id");
            e.HasIndex(x => new { x.ClientId, x.Name, x.Language })
                .IsUnique()
                .HasDatabaseName("ux_whatsapp_templates_client_name_language");
        });

        // ---------------------------------------------------------------
        // campaigns
        // ---------------------------------------------------------------
        modelBuilder.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns", "marketing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.CampaignType).HasColumnName("campaign_type").HasMaxLength(30).IsRequired();
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(30).IsRequired();
            e.Property(x => x.WhatsAppTemplateId).HasColumnName("whatsapp_template_id");
            e.Property(x => x.AudienceType).HasColumnName("audience_type").HasMaxLength(30).IsRequired();
            e.Property(x => x.AudienceFilters).HasColumnName("audience_filters").HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ScheduledAt).HasColumnName("scheduled_at");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WhatsAppTemplate).WithMany()
                .HasForeignKey(x => x.WhatsAppTemplateId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CampaignType);
            e.HasIndex(x => x.ScheduledAt);
            e.HasIndex(x => x.WhatsAppTemplateId);
        });

        // ---------------------------------------------------------------
        // campaign_recipients
        // ---------------------------------------------------------------
        modelBuilder.Entity<CampaignRecipient>(e =>
        {
            e.ToTable("campaign_recipients", "marketing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.CampaignId).HasColumnName("campaign_id");
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(200);
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(50).IsRequired();
            e.Property(x => x.VariablesJson).HasColumnName("variables_json").HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.SkipReason).HasColumnName("skip_reason").HasMaxLength(50);
            e.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
            e.Property(x => x.FailureReason).HasColumnName("failure_reason");
            e.Property(x => x.QueuedAt).HasColumnName("queued_at");
            e.Property(x => x.SentAt).HasColumnName("sent_at");
            e.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            e.Property(x => x.ReadAt).HasColumnName("read_at");
            e.Property(x => x.RepliedAt).HasColumnName("replied_at");
            e.Property(x => x.BookedAt).HasColumnName("booked_at");
            e.Property(x => x.FailedAt).HasColumnName("failed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Campaign).WithMany(c => c.Recipients)
                .HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Contact).WithMany()
                .HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Booking).WithMany()
                .HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.ContactId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ExternalMessageId);
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.CampaignId, x.ContactId })
                .IsUnique()
                .HasDatabaseName("ux_campaign_recipients_campaign_contact");
        });

        // ---------------------------------------------------------------
        // whatsapp_health_events
        // ---------------------------------------------------------------
        modelBuilder.Entity<WhatsAppHealthEvent>(e =>
        {
            e.ToTable("whatsapp_health_events", "channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ChannelIntegrationId).HasColumnName("channel_integration_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50);
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(100);
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.RawMetadataJson).HasColumnName("raw_metadata").HasColumnType("jsonb");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne(x => x.ChannelIntegration).WithMany()
                .HasForeignKey(x => x.ChannelIntegrationId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.ChannelIntegrationId);
            e.HasIndex(x => x.OccurredAt);
            // Practical idempotency for webhook retries — see WhatsAppHealthService.ApplyHealthEventAsync's
            // doc comment (Meta doesn't provide a stable event id for these).
            e.HasIndex(x => new { x.ChannelIntegrationId, x.EventType, x.OccurredAt })
                .IsUnique()
                .HasDatabaseName("ux_whatsapp_health_events_connection_type_occurred");
        });

        // ---------------------------------------------------------------
        // Structured availability (availability_rules / booking_settings / availability_exceptions)
        // — see Business/Services/Bookings/AvailabilityService.cs.
        // ---------------------------------------------------------------
        modelBuilder.Entity<AvailabilityRule>(e =>
        {
            e.ToTable("availability_rules", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.DayOfWeek).HasColumnName("day_of_week");
            e.Property(x => x.IsOpen).HasColumnName("is_open");
            e.Property(x => x.StartTime).HasColumnName("start_time");
            e.Property(x => x.EndTime).HasColumnName("end_time");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookingSettings>(e =>
        {
            e.ToTable("booking_settings", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.DefaultBookingDurationMinutes).HasColumnName("default_booking_duration_minutes");
            e.Property(x => x.BufferMinutes).HasColumnName("buffer_minutes");
            e.Property(x => x.MinimumBookingNoticeMinutes).HasColumnName("minimum_booking_notice_minutes");
            e.Property(x => x.MaximumAdvanceBookingDays).HasColumnName("maximum_advance_booking_days");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InboxSettings>(e =>
        {
            e.ToTable("inbox_settings", "crm");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ChannelIntegrationId).HasColumnName("channel_integration_id");
            e.Property(x => x.AiBotNewConversations).HasColumnName("ai_bot_new_conversations");
            e.Property(x => x.AiBotReopenedConversations).HasColumnName("ai_bot_reopened_conversations");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChannelIntegration>().WithMany().HasForeignKey(x => x.ChannelIntegrationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClientId).IsUnique().HasFilter("channel_integration_id is null");
            e.HasIndex(x => x.ChannelIntegrationId).IsUnique().HasFilter("channel_integration_id is not null");
        });

        modelBuilder.Entity<AvailabilityException>(e =>
        {
            e.ToTable("availability_exceptions", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.IsClosed).HasColumnName("is_closed");
            e.Property(x => x.StartTime).HasColumnName("start_time");
            e.Property(x => x.EndTime).HasColumnName("end_time");
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------------------------------------------------------
        // client_users — links a client or member user to their client. One ACTIVE row per user is a rule in
        // code (MemberService / MemberAdminService), not an index. CurrentClientContext resolves the current client
        // from it on every request.
        // ---------------------------------------------------------------
        modelBuilder.Entity<ClientUser>(e =>
        {
            e.ToTable("client_users", "identity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            // No CLR nav property to AppUser (scalar FK, like the rest of this schema) — still a real FK constraint.
            e.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ClientId, x.UserId })
                .IsUnique()
                .HasDatabaseName("ux_client_users_client_user");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_client_users_user_id");
        });

        // ---------------------------------------------------------------
        // account_tokens — one-time emailed links (password reset, member invite); only the token's hash is stored.
        // ---------------------------------------------------------------
        modelBuilder.Entity<AccountToken>(e =>
        {
            e.ToTable("account_tokens", "identity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(20).IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
            e.Property(x => x.FullName).HasColumnName("full_name");
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.UsedAt).HasColumnName("used_at");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_account_tokens_token_hash");
        });

        // ---------------------------------------------------------------
        // reminder_settings / booking_reminders — booking reminders (BookingReminderWorker).
        // ---------------------------------------------------------------
        modelBuilder.Entity<ReminderSettings>(e =>
        {
            e.ToTable("reminder_settings", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.FirstHoursBefore).HasColumnName("first_hours_before");
            e.Property(x => x.SecondHoursBefore).HasColumnName("second_hours_before");
            e.Property(x => x.TemplatesJson).HasColumnName("templates").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClientId).IsUnique();
        });

        modelBuilder.Entity<BookingReminder>(e =>
        {
            e.ToTable("booking_reminders", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(10).IsRequired();
            e.Property(x => x.ScheduledStart).HasColumnName("scheduled_start");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(10).IsRequired();
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(60);
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(30);
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.BookingId, x.Kind, x.ScheduledStart }).IsUnique().HasDatabaseName("ux_booking_reminders_once");
        });

        // ---------------------------------------------------------------
        // notifications — the client's notification bell. See Business/Contracts/Services/Notifications/INotificationService.cs.
        // ---------------------------------------------------------------
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications", "activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.TextArgs).HasColumnName("text_args").IsRequired();
            e.Property(x => x.ContactId).HasColumnName("contact_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.ChannelIntegrationId).HasColumnName("channel_integration_id");
            e.Property(x => x.Link).HasColumnName("link").HasMaxLength(300);
            e.Property(x => x.IsRead).HasColumnName("is_read");
            e.Property(x => x.ReadAt).HasColumnName("read_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            // Contact/Conversation/Booking/ChannelIntegration are all "set null" on delete in the DB — no CLR
            // navigation to any of them (matches Contact.AssignedUserId's scalar-FK-only pattern); a notification
            // outliving the row it points at just shows a dead link, never blocks the delete.

            e.HasIndex(x => new { x.ClientId, x.CreatedAt }).HasDatabaseName("ix_notifications_client_created");
            e.HasIndex(x => x.ClientId).HasFilter("is_read = false").HasDatabaseName("ix_notifications_client_unread");
        });

        // ---------------------------------------------------------------
        // Calendar Integrations — see Business/Contracts/Services/Calendars/ICalendarIntegrationService.cs / ICalendarSyncNotifier.cs.
        // ---------------------------------------------------------------
        modelBuilder.Entity<CalendarIntegration>(e =>
        {
            e.ToTable("calendar_integrations", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ExternalConnectionRef).HasColumnName("external_connection_ref").HasMaxLength(200);
            e.Property(x => x.AccountDisplayName).HasColumnName("account_display_name").HasMaxLength(200);
            e.Property(x => x.SelectedCalendarId).HasColumnName("selected_calendar_id").HasMaxLength(200);
            e.Property(x => x.SelectedCalendarName).HasColumnName("selected_calendar_name").HasMaxLength(200);
            e.Property(x => x.SyncEnabled).HasColumnName("sync_enabled");
            e.Property(x => x.IsHealthy).HasColumnName("is_healthy");
            e.Property(x => x.LastProblemMessage).HasColumnName("last_problem_message");
            e.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
            e.Property(x => x.AccessToken).HasColumnName("access_token");
            e.Property(x => x.RefreshToken).HasColumnName("refresh_token");
            e.Property(x => x.TokenExpiresAt).HasColumnName("token_expires_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Calendars).WithOne().HasForeignKey(x => x.CalendarIntegrationId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ClientId, x.Provider }).IsUnique().HasDatabaseName("ux_calendar_integrations_client_provider");
        });

        modelBuilder.Entity<CalendarIntegrationCalendar>(e =>
        {
            e.ToTable("calendar_integration_calendars", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CalendarIntegrationId).HasColumnName("calendar_integration_id");
            e.Property(x => x.ExternalCalendarId).HasColumnName("external_calendar_id").HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.IsPrimary).HasColumnName("is_primary");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasIndex(x => new { x.CalendarIntegrationId, x.ExternalCalendarId }).IsUnique()
                .HasDatabaseName("ux_calendar_integration_calendars_ext");
        });

        modelBuilder.Entity<BookingCalendarSync>(e =>
        {
            e.ToTable("booking_calendar_syncs", "scheduling");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.CalendarIntegrationId).HasColumnName("calendar_integration_id");
            e.Property(x => x.ExternalEventId).HasColumnName("external_event_id").HasMaxLength(200);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.LastRequestId).HasColumnName("last_request_id");
            e.Property(x => x.LastOperation).HasColumnName("last_operation").HasMaxLength(10);
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CalendarIntegration>().WithMany().HasForeignKey(x => x.CalendarIntegrationId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.BookingId, x.CalendarIntegrationId }).IsUnique()
                .HasDatabaseName("ux_booking_calendar_syncs_booking_integration");
        });

        // ---------------------------------------------------------------
        // TikTok Login Kit — see Business/Contracts/Services/TikTok/ITikTokIntegrationService.cs / ITikTokProviderClient.cs.
        // ---------------------------------------------------------------
        modelBuilder.Entity<TikTokIntegration>(e =>
        {
            e.ToTable("tiktok_integrations", "channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.OpenId).HasColumnName("open_id").HasMaxLength(200);
            e.Property(x => x.UnionId).HasColumnName("union_id").HasMaxLength(200);
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            e.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(500);
            e.Property(x => x.AccessToken).HasColumnName("access_token");
            e.Property(x => x.RefreshToken).HasColumnName("refresh_token");
            e.Property(x => x.TokenExpiresAt).HasColumnName("token_expires_at");
            e.Property(x => x.RefreshTokenExpiresAt).HasColumnName("refresh_token_expires_at");
            e.Property(x => x.IsHealthy).HasColumnName("is_healthy");
            e.Property(x => x.LastProblemMessage).HasColumnName("last_problem_message");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClientId).IsUnique().HasDatabaseName("ux_tiktok_integrations_client");
        });

        // ---------------------------------------------------------------
        // knowledge_documents / knowledge_chunks — the client Knowledge Base. The chunks table also
        // has a pgvector `embedding` column that is intentionally NOT mapped here (see
        // KnowledgeChunk's doc comment): chunks are written/searched with raw SQL.
        // ---------------------------------------------------------------
        modelBuilder.Entity<KnowledgeDocument>(e =>
        {
            e.ToTable("knowledge_documents", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255);
            e.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(100);
            e.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(2000);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.IsActive);
        });

        // Website scraping (see Integrations/Knowledge/WebScraping): crawl STATE only — no text, no vectors.
        modelBuilder.Entity<KnowledgeWebsiteSource>(e =>
        {
            e.ToTable("knowledge_website_sources", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.StartUrl).HasColumnName("start_url").HasMaxLength(2000).IsRequired();
            e.Property(x => x.NormalizedStartUrl).HasColumnName("normalized_start_url").HasMaxLength(2000).IsRequired();
            e.Property(x => x.Host).HasColumnName("host").HasMaxLength(255).IsRequired();
            e.Property(x => x.CrawlMode).HasColumnName("crawl_mode").HasMaxLength(20).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
            e.Property(x => x.LastScrapedAt).HasColumnName("last_scraped_at");
            e.Property(x => x.BoilerplateBlockHashes).HasColumnName("boilerplate_block_hashes").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClientId, x.NormalizedStartUrl }).IsUnique().HasDatabaseName("ux_kws_client_start_url");
            e.HasIndex(x => x.ClientId).HasDatabaseName("ix_kws_client_id");
        });

        modelBuilder.Entity<KnowledgeWebsitePage>(e =>
        {
            e.ToTable("knowledge_website_pages", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.WebsiteSourceId).HasColumnName("website_source_id");
            e.Property(x => x.Url).HasColumnName("url").HasMaxLength(2000).IsRequired();
            e.Property(x => x.NormalizedUrl).HasColumnName("normalized_url").HasMaxLength(2000).IsRequired();
            e.Property(x => x.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(2000);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
            e.Property(x => x.HttpStatus).HasColumnName("http_status");
            e.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(200);
            e.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            e.Property(x => x.ETag).HasColumnName("etag").HasMaxLength(500);
            e.Property(x => x.LastModifiedHeader).HasColumnName("last_modified_header").HasMaxLength(100);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.FailureReason).HasColumnName("failure_reason");
            e.Property(x => x.Depth).HasColumnName("depth");
            e.Property(x => x.KnowledgeDocumentId).HasColumnName("knowledge_document_id");
            e.Property(x => x.DuplicateOfPageId).HasColumnName("duplicate_of_page_id");
            e.Property(x => x.Links).HasColumnName("links").HasColumnType("jsonb");
            e.Property(x => x.MissingCount).HasColumnName("missing_count");
            e.Property(x => x.RemovedAt).HasColumnName("removed_at");
            e.Property(x => x.FirstDiscoveredAt).HasColumnName("first_discovered_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.Property(x => x.LastScrapedAt).HasColumnName("last_scraped_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<KnowledgeWebsiteSource>().WithMany().HasForeignKey(x => x.WebsiteSourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<KnowledgeDocument>().WithMany().HasForeignKey(x => x.KnowledgeDocumentId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WebsiteSourceId, x.NormalizedUrl }).IsUnique().HasDatabaseName("ux_kwp_source_normalized_url");
            e.HasIndex(x => x.ClientId).HasDatabaseName("ix_kwp_client_id");
            e.HasIndex(x => new { x.WebsiteSourceId, x.Status }).HasDatabaseName("ix_kwp_source_status");
        });

        modelBuilder.Entity<KnowledgeWebsiteScrapeRun>(e =>
        {
            e.ToTable("knowledge_website_scrape_runs", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.WebsiteSourceId).HasColumnName("website_source_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.PagesDiscovered).HasColumnName("pages_discovered");
            e.Property(x => x.PagesProcessed).HasColumnName("pages_processed");
            e.Property(x => x.PagesIndexed).HasColumnName("pages_indexed");
            e.Property(x => x.PagesNew).HasColumnName("pages_new");
            e.Property(x => x.PagesChanged).HasColumnName("pages_changed");
            e.Property(x => x.PagesUnchanged).HasColumnName("pages_unchanged");
            e.Property(x => x.PagesSkipped).HasColumnName("pages_skipped");
            e.Property(x => x.PagesDuplicate).HasColumnName("pages_duplicate");
            e.Property(x => x.PagesFailed).HasColumnName("pages_failed");
            e.Property(x => x.PagesRemoved).HasColumnName("pages_removed");
            e.Property(x => x.ErrorSummary).HasColumnName("error_summary");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<KnowledgeWebsiteSource>().WithMany().HasForeignKey(x => x.WebsiteSourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClientId).HasDatabaseName("ix_kwr_client_id");
        });

        modelBuilder.Entity<KnowledgeChunk>(e =>
        {
            e.ToTable("knowledge_chunks", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.KnowledgeDocumentId).HasColumnName("knowledge_document_id");
            e.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Document).WithMany(d => d.Chunks)
                .HasForeignKey(x => x.KnowledgeDocumentId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.KnowledgeDocumentId);
        });

        // Knowledge Retrieval Benchmark (see Integrations/Knowledge/Benchmark). Standalone diagnostic tables: the
        // expected document/chunk ids are deliberately plain columns, not foreign keys, so benchmark data can
        // never block or slow production ingestion (stale cases are detected in code instead).
        modelBuilder.Entity<KnowledgeRetrievalBenchmarkCase>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_cases", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Question).HasColumnName("question").IsRequired();
            e.Property(x => x.ExpectedDocumentId).HasColumnName("expected_document_id");
            e.Property(x => x.ExpectedChunkId).HasColumnName("expected_chunk_id");
            e.Property(x => x.CaseType).HasColumnName("case_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.IsReviewed).HasColumnName("is_reviewed");
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(x => x.GenerationId).HasColumnName("generation_id");
            e.Property(x => x.SourceChunkHash).HasColumnName("source_chunk_hash").HasMaxLength(64);
            e.Property(x => x.SourceChunkPreview).HasColumnName("source_chunk_preview").HasMaxLength(400);
            e.Property(x => x.SourceDocumentTitle).HasColumnName("source_document_title").HasMaxLength(200);
            e.Property(x => x.IsStale).HasColumnName("is_stale");
            e.Property(x => x.StaleReason).HasColumnName("stale_reason").HasMaxLength(30);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClientId).HasDatabaseName("ix_kbc_client_id");
        });

        modelBuilder.Entity<KnowledgeRetrievalBenchmarkGeneration>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_generations", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); // the app supplies it (it is the generationId)
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ChunksSent).HasColumnName("chunks_sent");
            e.Property(x => x.SentChunksJson).HasColumnName("sent_chunks").HasColumnType("jsonb");
            e.Property(x => x.QuestionsReturned).HasColumnName("questions_returned");
            e.Property(x => x.CasesCreated).HasColumnName("cases_created");
            e.Property(x => x.RejectedCount).HasColumnName("rejected_count");
            e.Property(x => x.RejectedJson).HasColumnName("rejected_json").HasColumnType("jsonb");
            e.Property(x => x.RawResponse).HasColumnName("raw_response");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClientId, x.CreatedAt }).HasDatabaseName("ix_kbg_client_created");
        });

        modelBuilder.Entity<KnowledgeRetrievalBenchmarkRun>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_runs", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.CaseScope).HasColumnName("case_scope").HasMaxLength(20).IsRequired();
            e.Property(x => x.GenerationId).HasColumnName("generation_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.TotalCases).HasColumnName("total_cases");
            e.Property(x => x.ProcessedCases).HasColumnName("processed_cases");
            e.Property(x => x.ScoredCases).HasColumnName("scored_cases");
            e.Property(x => x.StaleCases).HasColumnName("stale_cases");
            e.Property(x => x.ErrorCases).HasColumnName("error_cases");
            e.Property(x => x.ChunkTop1Accuracy).HasColumnName("chunk_top1_accuracy");
            e.Property(x => x.ChunkTop3Accuracy).HasColumnName("chunk_top3_accuracy");
            e.Property(x => x.ChunkTop5Accuracy).HasColumnName("chunk_top5_accuracy");
            e.Property(x => x.DocumentTop1Accuracy).HasColumnName("document_top1_accuracy");
            e.Property(x => x.DocumentTop3Accuracy).HasColumnName("document_top3_accuracy");
            e.Property(x => x.DocumentTop5Accuracy).HasColumnName("document_top5_accuracy");
            e.Property(x => x.ChunkMrr).HasColumnName("chunk_mrr");
            e.Property(x => x.DocumentMrr).HasColumnName("document_mrr");
            e.Property(x => x.AverageLatencyMs).HasColumnName("average_latency_ms");
            e.Property(x => x.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(100);
            e.Property(x => x.VectorDimension).HasColumnName("vector_dimension");
            e.Property(x => x.ChunkSizeTokens).HasColumnName("chunk_size_tokens");
            e.Property(x => x.ChunkOverlapTokens).HasColumnName("chunk_overlap_tokens");
            e.Property(x => x.SimilarityMethod).HasColumnName("similarity_method").HasMaxLength(30);
            e.Property(x => x.TopK).HasColumnName("top_k");
            e.Property(x => x.MinimumSimilarity).HasColumnName("minimum_similarity");
            e.Property(x => x.IndexedChunkCount).HasColumnName("indexed_chunk_count");
            e.Property(x => x.AvgChunkChars).HasColumnName("avg_chunk_chars");
            e.Property(x => x.ErrorSummary).HasColumnName("error_summary");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClientId, x.CreatedAt }).HasDatabaseName("ix_kbr_client_created");
        });

        modelBuilder.Entity<KnowledgeRetrievalBenchmarkResult>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_results", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.BenchmarkRunId).HasColumnName("benchmark_run_id");
            e.Property(x => x.BenchmarkCaseId).HasColumnName("benchmark_case_id");
            e.Property(x => x.Question).HasColumnName("question").IsRequired();
            e.Property(x => x.ExpectedDocumentId).HasColumnName("expected_document_id");
            e.Property(x => x.ExpectedChunkId).HasColumnName("expected_chunk_id");
            e.Property(x => x.ExpectedDocumentTitle).HasColumnName("expected_document_title").HasMaxLength(200);
            e.Property(x => x.ExpectedChunkPreview).HasColumnName("expected_chunk_preview").HasMaxLength(400);
            e.Property(x => x.GenerationId).HasColumnName("generation_id");
            e.Property(x => x.ExpectedChunkRank).HasColumnName("expected_chunk_rank");
            e.Property(x => x.ExpectedDocumentBestRank).HasColumnName("expected_document_best_rank");
            e.Property(x => x.ExpectedChunkScore).HasColumnName("expected_chunk_score");
            e.Property(x => x.ExpectedBelowThreshold).HasColumnName("expected_below_threshold");
            e.Property(x => x.ChunkTop1Pass).HasColumnName("chunk_top1_pass");
            e.Property(x => x.ChunkTop3Pass).HasColumnName("chunk_top3_pass");
            e.Property(x => x.ChunkTop5Pass).HasColumnName("chunk_top5_pass");
            e.Property(x => x.DocumentTop1Pass).HasColumnName("document_top1_pass");
            e.Property(x => x.DocumentTop3Pass).HasColumnName("document_top3_pass");
            e.Property(x => x.DocumentTop5Pass).HasColumnName("document_top5_pass");
            e.Property(x => x.ResultClassification).HasColumnName("result_classification").HasMaxLength(30).IsRequired();
            e.Property(x => x.StaleReason).HasColumnName("stale_reason").HasMaxLength(30);
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.ReturnedCount).HasColumnName("returned_count");
            e.Property(x => x.RetrievedJson).HasColumnName("retrieved_json").HasColumnType("jsonb");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<KnowledgeRetrievalBenchmarkRun>().WithMany().HasForeignKey(x => x.BenchmarkRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<KnowledgeRetrievalBenchmarkCase>().WithMany().HasForeignKey(x => x.BenchmarkCaseId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.BenchmarkRunId).HasDatabaseName("ix_kbres_run");
            e.HasIndex(x => x.ClientId).HasDatabaseName("ix_kbres_client_id");
            e.HasIndex(x => new { x.BenchmarkCaseId, x.CreatedAt }).HasDatabaseName("ix_kbres_case_created");
        });

        // One settings row per client (unique client_id) — see KnowledgeSearchSettings.
        modelBuilder.Entity<KnowledgeSearchSettings>(e =>
        {
            e.ToTable("knowledge_search_settings", "knowledge");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(100).IsRequired();
            e.Property(x => x.VectorDimension).HasColumnName("vector_dimension");
            e.Property(x => x.ChunkSizeTokens).HasColumnName("chunk_size_tokens");
            e.Property(x => x.ChunkOverlapTokens).HasColumnName("chunk_overlap_tokens");
            e.Property(x => x.SimilarityMethod).HasColumnName("similarity_method").HasMaxLength(30).IsRequired();
            e.Property(x => x.TopK).HasColumnName("top_k");
            e.Property(x => x.MinimumSimilarity).HasColumnName("minimum_similarity");
            e.Property(x => x.VectorIndexType).HasColumnName("vector_index_type").HasMaxLength(30).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClientId)
                .IsUnique()
                .HasDatabaseName("ux_knowledge_search_settings_client_id");
        });

        // Stored settings (config.settings), read through IConfigManager — see ConfigSetting and ConfigDefaults.
        modelBuilder.Entity<ConfigSetting>(e =>
        {
            e.ToTable("settings", "config");
            e.HasKey(x => new { x.Section, x.Key });
            e.Property(x => x.Section).HasColumnName("section").HasMaxLength(100);
            e.Property(x => x.Key).HasColumnName("key").HasMaxLength(100);
            e.Property(x => x.Value).HasColumnName("value").IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // Per-minute API timings (activity.api_route_stats) — see ApiRouteStat and ApiTimingFlushJob.
        modelBuilder.Entity<ApiRouteStat>(e =>
        {
            e.ToTable("api_route_stats", "activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.BucketStart).HasColumnName("bucket_start");
            e.Property(x => x.Method).HasColumnName("method").HasMaxLength(10).IsRequired();
            e.Property(x => x.Route).HasColumnName("route").HasMaxLength(300).IsRequired();
            e.Property(x => x.RequestCount).HasColumnName("request_count");
            e.Property(x => x.ErrorCount).HasColumnName("error_count");
            e.Property(x => x.ClientErrorCount).HasColumnName("client_error_count");
            e.Property(x => x.SlowCount).HasColumnName("slow_count");
            e.Property(x => x.TotalMs).HasColumnName("total_ms");
            e.Property(x => x.P95Ms).HasColumnName("p95_ms");
            e.Property(x => x.MaxMs).HasColumnName("max_ms");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.BucketStart);
        });

        // Saved warnings and errors (activity.app_logs) — see AppLog and AppLogFlushJob. No foreign keys on purpose.
        modelBuilder.Entity<AppLog>(e =>
        {
            e.ToTable("app_logs", "activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.Level).HasColumnName("level").HasMaxLength(12).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(300).IsRequired();
            e.Property(x => x.Message).HasColumnName("message").IsRequired();
            e.Property(x => x.MessageTemplate).HasColumnName("message_template").HasMaxLength(1000);
            e.Property(x => x.ExceptionType).HasColumnName("exception_type").HasMaxLength(300);
            e.Property(x => x.ExceptionMessage).HasColumnName("exception_message");
            e.Property(x => x.StackTrace).HasColumnName("stack_trace");
            e.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(16).IsRequired();
            e.Property(x => x.RepeatCount).HasColumnName("repeat_count");
            e.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(32);
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.Method).HasColumnName("method").HasMaxLength(10);
            e.Property(x => x.Route).HasColumnName("route").HasMaxLength(300);
            e.Property(x => x.Instance).HasColumnName("instance").HasMaxLength(100);
            e.Property(x => x.Properties).HasColumnName("properties").HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.Fingerprint, x.OccurredAt });
        });

        // Message trace steps (activity.message_trace_steps) — see MessageTraceStep and MessageTraceFlushJob. No foreign keys.
        modelBuilder.Entity<MessageTraceStep>(e =>
        {
            e.ToTable("message_trace_steps", "activity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(32).IsRequired();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.Step).HasColumnName("step").HasMaxLength(60).IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(12).IsRequired();
            e.Property(x => x.DurationMs).HasColumnName("duration_ms");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(200);
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(50);
            e.Property(x => x.N8nExecutionId).HasColumnName("n8n_execution_id").HasMaxLength(100);
            e.Property(x => x.Detail).HasColumnName("detail").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Instance).HasColumnName("instance").HasMaxLength(100);
            e.HasIndex(x => new { x.TraceId, x.OccurredAt });
            e.HasIndex(x => x.OccurredAt);
        });

        // ---------------------------------------------------------------
        // Subscriptions & usage billing — all in the Postgres schema "billing" (billing.*). See Billing/ (module) and
        // docs/billing.md. Money columns are numeric(18,6) (plan price/credit numeric(18,2)); the CHECKs, the
        // rate-overlap exclusion constraint and the immutability triggers live in schema.sql only.
        // ---------------------------------------------------------------
        modelBuilder.Entity<ChannelAccountBillingSettings>(e =>
        {
            e.ToTable("channel_account_settings", "billing");
            e.HasKey(x => x.ChannelIntegrationId);
            e.Property(x => x.ChannelIntegrationId).HasColumnName("channel_integration_id").ValueGeneratedNever();
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.ProviderBilling).HasColumnName("provider_billing").HasMaxLength(30);
            e.Property(x => x.OmniUsageBilling).HasColumnName("omni_usage_billing");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<ChannelIntegration>().WithOne().HasForeignKey<ChannelAccountBillingSettings>(x => x.ChannelIntegrationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriptionPlan>(e =>
        {
            e.ToTable("plans", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 2);
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.BillingPeriod).HasColumnName("billing_period").HasMaxLength(10).IsRequired();
            e.Property(x => x.IncludedUsageCredit).HasColumnName("included_usage_credit").HasPrecision(18, 2);
            e.Property(x => x.RateCardId).HasColumnName("rate_card_id");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasMany(x => x.Entitlements).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BillingRateCard>().WithMany().HasForeignKey(x => x.RateCardId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<SubscriptionPlanEntitlement>(e =>
        {
            e.ToTable("plan_entitlements", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.EntitlementKey).HasColumnName("entitlement_key").HasMaxLength(60).IsRequired();
            e.Property(x => x.Value).HasColumnName("value").HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasIndex(x => new { x.PlanId, x.EntitlementKey }).IsUnique()
                .HasDatabaseName("ux_plan_entitlements_key");
        });

        modelBuilder.Entity<ClientSubscription>(e =>
        {
            e.ToTable("subscriptions", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.CurrentPeriodStart).HasColumnName("current_period_start");
            e.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            e.Property(x => x.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end");
            e.Property(x => x.PastDueSince).HasColumnName("past_due_since");
            e.Property(x => x.EndedAt).HasColumnName("ended_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ClientId).IsUnique();
        });

        modelBuilder.Entity<BillingAccount>(e =>
        {
            e.ToTable("accounts", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.WalletBalance).HasColumnName("wallet_balance").HasPrecision(18, 6);
            e.Property(x => x.IncludedCreditBalance).HasColumnName("included_credit_balance").HasPrecision(18, 6);
            e.Property(x => x.ReservedAmount).HasColumnName("reserved_amount").HasPrecision(18, 6);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Ignore(x => x.Spendable);

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClientId).IsUnique();
        });

        modelBuilder.Entity<BillingRateCard>(e =>
        {
            e.ToTable("rate_cards", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<BillingRate>(e =>
        {
            e.ToTable("rates", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RateCardId).HasColumnName("rate_card_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(60).IsRequired();
            e.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Operator).HasColumnName("operator").HasMaxLength(60);
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30);
            e.Property(x => x.ProviderBilling).HasColumnName("provider_billing").HasMaxLength(30);
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
            e.Property(x => x.ProviderCost).HasColumnName("provider_cost").HasPrecision(18, 6);
            e.Property(x => x.ClientRate).HasColumnName("client_rate").HasPrecision(18, 6);
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
            e.Property(x => x.EffectiveTo).HasColumnName("effective_to");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<BillingRateCard>().WithMany().HasForeignKey(x => x.RateCardId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.EventType, x.RateCardId }).HasDatabaseName("ix_rates_lookup");
        });

        modelBuilder.Entity<BillingUsageRecord>(e =>
        {
            e.ToTable("usage_records", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.BillingAccountId).HasColumnName("billing_account_id");
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(60).IsRequired();
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(30).IsRequired();
            e.Property(x => x.ChannelIntegrationId).HasColumnName("channel_integration_id");
            e.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6);
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20);
            e.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Operator).HasColumnName("operator").HasMaxLength(60);
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30);
            e.Property(x => x.ProviderBilling).HasColumnName("provider_billing").HasMaxLength(30).IsRequired();
            e.Property(x => x.RateId).HasColumnName("rate_id");
            e.Property(x => x.RateCardId).HasColumnName("rate_card_id");
            e.Property(x => x.RateSource).HasColumnName("rate_source").HasMaxLength(20);
            e.Property(x => x.UnitProviderCost).HasColumnName("unit_provider_cost").HasPrecision(18, 6);
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 6);
            e.Property(x => x.ProviderCost).HasColumnName("provider_cost").HasPrecision(18, 6);
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6);
            e.Property(x => x.ReservedAmount).HasColumnName("reserved_amount").HasPrecision(18, 6);
            e.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasPrecision(18, 6);
            e.Property(x => x.WalletAmount).HasColumnName("wallet_amount").HasPrecision(18, 6);
            e.Property(x => x.RefundedAmount).HasColumnName("refunded_amount").HasPrecision(18, 6);
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.ChargeStatus).HasColumnName("charge_status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ProviderOutcome).HasColumnName("provider_outcome").HasMaxLength(20).IsRequired();
            e.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(60);
            e.Property(x => x.ReleaseReason).HasColumnName("release_reason").HasMaxLength(100);
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.CampaignId).HasColumnName("campaign_id");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(30).IsRequired();
            e.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(200);
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.ReservedAt).HasColumnName("reserved_at");
            e.Property(x => x.SettledAt).HasColumnName("settled_at");
            e.Property(x => x.ReleasedAt).HasColumnName("released_at");
            e.Property(x => x.RefundedAt).HasColumnName("refunded_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
            // A used rate can't be deleted (no action in the DB).
            e.HasOne<BillingRate>().WithMany().HasForeignKey(x => x.RateId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<BillingRateCard>().WithMany().HasForeignKey(x => x.RateCardId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ClientId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_usage_records_key");
            e.HasIndex(x => new { x.ClientId, x.OccurredAt }).HasDatabaseName("ix_usage_records_client_occurred");
        });

        modelBuilder.Entity<BillingLedgerEntry>(e =>
        {
            e.ToTable("ledger_entries", "billing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Seq).HasColumnName("seq").ValueGeneratedOnAdd();
            e.Property(x => x.ClientId).HasColumnName("client_id");
            e.Property(x => x.BillingAccountId).HasColumnName("billing_account_id");
            e.Property(x => x.EntryType).HasColumnName("entry_type").HasMaxLength(40).IsRequired();
            e.Property(x => x.BalanceType).HasColumnName("balance_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6);
            e.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasPrecision(18, 6);
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
            e.Property(x => x.UsageRecordId).HasColumnName("usage_record_id");
            e.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(30).IsRequired();
            e.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(200);
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200);
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
            // Declared so EF inserts a new usage record / subscription before the ledger rows that point at it.
            e.HasOne<BillingUsageRecord>().WithMany().HasForeignKey(x => x.UsageRecordId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ClientSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ClientId, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_ledger_entries_key");
            e.HasIndex(x => new { x.ClientId, x.Seq }).HasDatabaseName("ix_ledger_entries_client_seq");
        });
    }
}
