using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Data;

/// <summary>
/// EF Core context mapped onto the schema owned by Database/schema.sql.
/// This context does NOT own migrations — the schema is created/altered
/// by running schema.sql directly (psql or the Supabase SQL editor), per
/// the plan's "use SQL migrations" principle. Every mapping below must
/// stay in sync with schema.sql by hand.
///
/// Inherits IdentityUserContext&lt;IdentityUser&gt; (not the full IdentityDbContext, which also adds
/// AspNetRoles/AspNetUserRoles/AspNetRoleClaims) — this MVP ignores roles completely (see
/// ClinicUser's doc comment), so there's no reason to carry role tables that will never be used.
/// Identity's own tables are remapped to snake_case below to match this schema's convention rather
/// than keeping EF's default PascalCase "AspNetUsers" etc.
/// </summary>
public class ApplicationDbContext : IdentityUserContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ProcedureBooking> ProcedureBookings => Set<ProcedureBooking>();
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
    public DbSet<ClinicUser> ClinicUsers => Set<ClinicUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Lets IdentityUserContext configure its own entities first (IdentityUser, IdentityUserClaim,
        // IdentityUserLogin, IdentityUserToken) before we remap their table/column names below.
        base.OnModelCreating(modelBuilder);

        // ---------------------------------------------------------------
        // Identity tables — remapped from EF's default PascalCase ("AspNetUsers" etc.) to this
        // schema's snake_case convention. See Database/schema.sql for the matching DDL.
        // ---------------------------------------------------------------
        modelBuilder.Entity<IdentityUser>(e =>
        {
            e.ToTable("identity_users");
            e.Property(u => u.Id).HasColumnName("id");
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
            e.ToTable("identity_user_claims");
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.UserId).HasColumnName("user_id");
            e.Property(c => c.ClaimType).HasColumnName("claim_type");
            e.Property(c => c.ClaimValue).HasColumnName("claim_value");
        });

        modelBuilder.Entity<IdentityUserLogin<string>>(e =>
        {
            e.ToTable("identity_user_logins");
            e.Property(l => l.LoginProvider).HasColumnName("login_provider");
            e.Property(l => l.ProviderKey).HasColumnName("provider_key");
            e.Property(l => l.ProviderDisplayName).HasColumnName("provider_display_name");
            e.Property(l => l.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<IdentityUserToken<string>>(e =>
        {
            e.ToTable("identity_user_tokens");
            e.Property(t => t.UserId).HasColumnName("user_id");
            e.Property(t => t.LoginProvider).HasColumnName("login_provider");
            e.Property(t => t.Name).HasColumnName("name");
            e.Property(t => t.Value).HasColumnName("value");
        });

        // ---------------------------------------------------------------
        // clinics
        // ---------------------------------------------------------------
        modelBuilder.Entity<Clinic>(e =>
        {
            e.ToTable("clinics");
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
            e.Property(x => x.ConsultationInfo).HasColumnName("consultation_info");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.Slug).IsUnique();
        });

        // ---------------------------------------------------------------
        // procedures
        // ---------------------------------------------------------------
        modelBuilder.Entity<Procedure>(e =>
        {
            e.ToTable("procedures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(100);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.ConsultationDuration).HasColumnName("consultation_duration");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Clinic).WithMany(c => c.Procedures)
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId);
        });

        // ---------------------------------------------------------------
        // leads
        // ---------------------------------------------------------------
        modelBuilder.Entity<Lead>(e =>
        {
            e.ToTable("leads");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.ProcedureId).HasColumnName("procedure_id");
            e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200);
            e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100);
            e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(50);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(100);
            e.Property(x => x.SourceDetail).HasColumnName("source_detail").HasMaxLength(200);
            e.Property(x => x.CampaignName).HasColumnName("campaign_name").HasMaxLength(200);
            e.Property(x => x.ExternalLeadId).HasColumnName("external_lead_id").HasMaxLength(200);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            e.Property(x => x.QualificationStatus).HasColumnName("qualification_status").HasMaxLength(50).IsRequired();
            e.Property(x => x.PreferredLanguage).HasColumnName("preferred_language").HasMaxLength(20);
            e.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(10);
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
            e.Property(x => x.DesiredTimeline).HasColumnName("desired_timeline").HasMaxLength(100);
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.AssignedStaffId).HasColumnName("assigned_staff_id");
            e.Property(x => x.MarketingOptIn).HasColumnName("marketing_opt_in");
            e.Property(x => x.OptedOutAt).HasColumnName("opted_out_at");
            e.Property(x => x.LastContactAt).HasColumnName("last_contact_at");
            e.Property(x => x.NextFollowupAt).HasColumnName("next_followup_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Clinic).WithMany(c => c.Leads)
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Procedure).WithMany()
                .HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => new { x.ClinicId, x.Phone });
            e.HasIndex(x => new { x.ClinicId, x.Email });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ProcedureId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.ClinicId, x.ExternalLeadId })
                .IsUnique()
                .HasFilter("external_lead_id IS NOT NULL")
                .HasDatabaseName("ux_leads_clinic_external_lead_id");
        });

        // ---------------------------------------------------------------
        // conversations
        // ---------------------------------------------------------------
        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.LeadId).HasColumnName("lead_id");
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

            e.HasOne(x => x.Lead).WithMany(l => l.Conversations)
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.LeadId);
            e.HasIndex(x => x.ServiceWindowExpiresAt);
        });

        // ---------------------------------------------------------------
        // messages
        // ---------------------------------------------------------------
        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.LeadId).HasColumnName("lead_id");
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

            e.HasOne(x => x.Conversation).WithMany(c => c.Messages)
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Lead).WithMany()
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.LeadId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.WhatsAppTemplateId);
            // Idempotency for webhook/n8n retries — see MessageService.IngestAsync.
            e.HasIndex(x => new { x.ClinicId, x.Channel, x.ExternalMessageId })
                .IsUnique()
                .HasFilter("external_message_id IS NOT NULL")
                .HasDatabaseName("ux_messages_clinic_channel_external_message_id");
        });

        // ---------------------------------------------------------------
        // appointments
        // ---------------------------------------------------------------
        modelBuilder.Entity<Appointment>(e =>
        {
            e.ToTable("appointments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.LeadId).HasColumnName("lead_id");
            e.Property(x => x.ProcedureId).HasColumnName("procedure_id");
            e.Property(x => x.AppointmentType).HasColumnName("appointment_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            e.Property(x => x.ScheduledStart).HasColumnName("scheduled_start").IsRequired();
            e.Property(x => x.ScheduledEnd).HasColumnName("scheduled_end");
            e.Property(x => x.LocationType).HasColumnName("location_type").HasMaxLength(30);
            e.Property(x => x.LocationName).HasColumnName("location_name").HasMaxLength(200);
            e.Property(x => x.ExternalCalendarId).HasColumnName("external_calendar_id").HasMaxLength(200);
            e.Property(x => x.ExternalEventId).HasColumnName("external_event_id").HasMaxLength(200);
            e.Property(x => x.AssignedStaffId).HasColumnName("assigned_staff_id");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Lead).WithMany(l => l.Appointments)
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Procedure).WithMany()
                .HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.LeadId);
            e.HasIndex(x => x.ScheduledStart);
            e.HasIndex(x => x.Status);
        });

        // ---------------------------------------------------------------
        // procedure_bookings
        // ---------------------------------------------------------------
        modelBuilder.Entity<ProcedureBooking>(e =>
        {
            e.ToTable("procedure_bookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.LeadId).HasColumnName("lead_id");
            e.Property(x => x.ProcedureId).HasColumnName("procedure_id");
            e.Property(x => x.AppointmentId).HasColumnName("appointment_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            e.Property(x => x.QuotedAmount).HasColumnName("quoted_amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.FinalAmount).HasColumnName("final_amount").HasColumnType("numeric(12,2)");
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
            e.Property(x => x.ProcedureDate).HasColumnName("procedure_date");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Lead).WithMany(l => l.ProcedureBookings)
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Procedure).WithMany()
                .HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Appointment).WithMany(a => a.ProcedureBookings)
                .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.LeadId);
            e.HasIndex(x => x.Status);
        });

        // ---------------------------------------------------------------
        // events
        // ---------------------------------------------------------------
        modelBuilder.Entity<EventLog>(e =>
        {
            e.ToTable("events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.LeadId).HasColumnName("lead_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.AppointmentId).HasColumnName("appointment_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(50);
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            // These weren't configured before — EventLog has no CLR nav properties for them, but EF
            // still needs to know the FK relationships exist (they do, in schema.sql) so it orders
            // inserts correctly: a brand-new Lead/Conversation/Appointment added in the same
            // SaveChangesAsync call as an EventLog row referencing it must insert first, and without
            // this EF has no dependency info to guarantee that ordering (see LeadService.CreateOrGetAsync
            // / GetOrCreateByPhoneAsync, both of which add a new Lead and log its creation event in
            // one call).
            e.HasOne<Clinic>().WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lead>().WithMany()
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Conversation>().WithMany()
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Appointment>().WithMany()
                .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.CreatedAt);
        });

        // ---------------------------------------------------------------
        // channel_integrations
        // ---------------------------------------------------------------
        modelBuilder.Entity<ChannelIntegration>(e =>
        {
            e.ToTable("channel_integrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne(x => x.Clinic).WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ClinicId, x.Channel })
                .IsUnique()
                .HasDatabaseName("ux_channel_integrations_clinic_channel");
            // A Meta phone_number_id must never map to more than one clinic — webhook routing
            // (phone_number_id -> channel_integration -> clinic_id) depends on this being
            // unambiguous. Filtered (not null only): most rows here are non-WhatsApp channels
            // (Instagram/Facebook) or WhatsApp rows not yet connected, neither of which sets this.
            e.HasIndex(x => x.PhoneNumberId)
                .IsUnique()
                .HasFilter("phone_number_id IS NOT NULL")
                .HasDatabaseName("ux_channel_integrations_phone_number_id");
            // WABA id is deliberately NOT unique — one WABA can contain multiple phone numbers, so
            // multiple channel_integrations rows (even across different clinics, in principle) can
            // legitimately share a whatsapp_business_id. Indexed for lookup speed only.
            e.HasIndex(x => x.WhatsAppBusinessId);
        });

        // ---------------------------------------------------------------
        // whatsapp_templates
        // ---------------------------------------------------------------
        modelBuilder.Entity<WhatsAppTemplate>(e =>
        {
            e.ToTable("whatsapp_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne(x => x.Clinic).WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.ChannelIntegrationId);
            e.HasIndex(x => new { x.ClinicId, x.MetaTemplateId })
                .HasDatabaseName("ix_whatsapp_templates_clinic_meta_template_id");
            e.HasIndex(x => new { x.ClinicId, x.Name, x.Language })
                .IsUnique()
                .HasDatabaseName("ux_whatsapp_templates_clinic_name_language");
        });

        // ---------------------------------------------------------------
        // campaigns
        // ---------------------------------------------------------------
        modelBuilder.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne(x => x.Clinic).WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WhatsAppTemplate).WithMany()
                .HasForeignKey(x => x.WhatsAppTemplateId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ClinicId);
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
            e.ToTable("campaign_recipients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.CampaignId).HasColumnName("campaign_id");
            e.Property(x => x.LeadId).HasColumnName("lead_id");
            e.Property(x => x.ConversationId).HasColumnName("conversation_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(200);
            e.Property(x => x.AppointmentId).HasColumnName("appointment_id");
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
            e.HasOne(x => x.Lead).WithMany()
                .HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Appointment).WithMany()
                .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.CampaignId);
            e.HasIndex(x => x.LeadId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ExternalMessageId);
            e.HasIndex(x => x.AppointmentId);
            e.HasIndex(x => new { x.CampaignId, x.LeadId })
                .IsUnique()
                .HasDatabaseName("ux_campaign_recipients_campaign_lead");
        });

        // ---------------------------------------------------------------
        // whatsapp_health_events
        // ---------------------------------------------------------------
        modelBuilder.Entity<WhatsAppHealthEvent>(e =>
        {
            e.ToTable("whatsapp_health_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.ChannelIntegrationId);
            e.HasIndex(x => x.OccurredAt);
            // Practical idempotency for webhook retries — see WhatsAppHealthService.ApplyHealthEventAsync's
            // doc comment (Meta doesn't provide a stable event id for these).
            e.HasIndex(x => new { x.ChannelIntegrationId, x.EventType, x.OccurredAt })
                .IsUnique()
                .HasDatabaseName("ux_whatsapp_health_events_connection_type_occurred");
        });

        // ---------------------------------------------------------------
        // clinic_users — links a logged-in Identity user to their clinic. See ClinicUser's doc
        // comment and Services/ICurrentClinicContext.cs, the one place that reads this table.
        // ---------------------------------------------------------------
        modelBuilder.Entity<ClinicUser>(e =>
        {
            e.ToTable("clinic_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne(x => x.Clinic).WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            // No CLR nav property to IdentityUser (matches this schema's minimalist pattern for
            // scalar FK columns elsewhere, e.g. Lead.AssignedStaffId) — still a real FK constraint.
            e.HasOne<IdentityUser>().WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ClinicId, x.UserId })
                .IsUnique()
                .HasDatabaseName("ux_clinic_users_clinic_user");
            // Enforces the MVP assumption CurrentClinicContext relies on (first active row wins) —
            // a user can have historical/inactive membership rows, but only one active one at a
            // time, so that lookup can never actually be ambiguous. Filtered so multiple inactive
            // rows for the same user don't collide.
            e.HasIndex(x => x.UserId)
                .IsUnique()
                .HasFilter("is_active = true")
                .HasDatabaseName("ux_clinic_users_user_active");
        });

        // ---------------------------------------------------------------
        // knowledge_documents / knowledge_chunks — the clinic Knowledge Base. The chunks table also
        // has a pgvector `embedding` column that is intentionally NOT mapped here (see
        // KnowledgeChunk's doc comment): chunks are written/searched with raw SQL.
        // ---------------------------------------------------------------
        modelBuilder.Entity<KnowledgeDocument>(e =>
        {
            e.ToTable("knowledge_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne(x => x.Clinic).WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.IsActive);
        });

        // Website scraping (see Integrations/Knowledge/WebScraping): crawl STATE only — no text, no vectors.
        modelBuilder.Entity<KnowledgeWebsiteSource>(e =>
        {
            e.ToTable("knowledge_website_sources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClinicId, x.NormalizedStartUrl }).IsUnique().HasDatabaseName("ux_kws_clinic_start_url");
            e.HasIndex(x => x.ClinicId).HasDatabaseName("ix_kws_clinic_id");
        });

        modelBuilder.Entity<KnowledgeWebsitePage>(e =>
        {
            e.ToTable("knowledge_website_pages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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
            e.HasIndex(x => x.ClinicId).HasDatabaseName("ix_kwp_clinic_id");
            e.HasIndex(x => new { x.WebsiteSourceId, x.Status }).HasDatabaseName("ix_kwp_source_status");
        });

        modelBuilder.Entity<KnowledgeWebsiteScrapeRun>(e =>
        {
            e.ToTable("knowledge_website_scrape_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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
            e.HasIndex(x => x.ClinicId).HasDatabaseName("ix_kwr_clinic_id");
        });

        modelBuilder.Entity<KnowledgeChunk>(e =>
        {
            e.ToTable("knowledge_chunks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
            e.Property(x => x.KnowledgeDocumentId).HasColumnName("knowledge_document_id");
            e.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasOne<Clinic>().WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Document).WithMany(d => d.Chunks)
                .HasForeignKey(x => x.KnowledgeDocumentId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId);
            e.HasIndex(x => x.KnowledgeDocumentId);
        });

        // Knowledge Retrieval Benchmark (see Integrations/Knowledge/Benchmark). Standalone diagnostic tables: the
        // expected document/chunk ids are deliberately plain columns, not foreign keys, so benchmark data can
        // never block or slow production ingestion (stale cases are detected in code instead).
        modelBuilder.Entity<KnowledgeRetrievalBenchmarkCase>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_cases");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ClinicId).HasDatabaseName("ix_kbc_clinic_id");
        });

        modelBuilder.Entity<KnowledgeRetrievalBenchmarkGeneration>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_generations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); // the app supplies it (it is the generationId)
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClinicId, x.CreatedAt }).HasDatabaseName("ix_kbg_clinic_created");
        });

        modelBuilder.Entity<KnowledgeRetrievalBenchmarkRun>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ClinicId, x.CreatedAt }).HasDatabaseName("ix_kbr_clinic_created");
        });

        modelBuilder.Entity<KnowledgeRetrievalBenchmarkResult>(e =>
        {
            e.ToTable("knowledge_retrieval_benchmark_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<KnowledgeRetrievalBenchmarkRun>().WithMany().HasForeignKey(x => x.BenchmarkRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<KnowledgeRetrievalBenchmarkCase>().WithMany().HasForeignKey(x => x.BenchmarkCaseId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.BenchmarkRunId).HasDatabaseName("ix_kbres_run");
            e.HasIndex(x => x.ClinicId).HasDatabaseName("ix_kbres_clinic_id");
            e.HasIndex(x => new { x.BenchmarkCaseId, x.CreatedAt }).HasDatabaseName("ix_kbres_case_created");
        });

        // One settings row per clinic (unique clinic_id) — see KnowledgeSearchSettings.
        modelBuilder.Entity<KnowledgeSearchSettings>(e =>
        {
            e.ToTable("knowledge_search_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClinicId).HasColumnName("clinic_id");
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

            e.HasOne(x => x.Clinic).WithMany()
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ClinicId)
                .IsUnique()
                .HasDatabaseName("ux_knowledge_search_settings_clinic_id");
        });
    }
}
