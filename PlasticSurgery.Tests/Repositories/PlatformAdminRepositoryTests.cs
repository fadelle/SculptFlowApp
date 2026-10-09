using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Repositories;
using PlasticSurgery.Persistence.Repositories.PlatformAdmin;

namespace PlasticSurgery.Tests.Repositories;

/// <summary>
/// The cross-clinic reads behind /api/platform-admin. Every test seeds its own clinic and filters by it, so rows from other
/// tests in the shared database never leak in; only the global totals are asserted as lower bounds.
/// </summary>
public class PlatformAdminRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private sealed record World(Clinic Clinic, Procedure Procedure, Lead Lead, Conversation Conversation, Message Inbound, Message Failed,
        Appointment Upcoming, ChannelIntegration WhatsApp);

    private async Task<World> SeedWorldAsync(string? clinicName = null)
    {
        var clinic = MakeClinic(clinicName);
        clinic.Email = "owner@" + clinic.Slug + ".test";
        var proc = MakeProcedure(clinic.Id, "Rhinoplasty");
        var lead = MakeLead(clinic.Id, "Alice Wong", "+9611", proc.Id);
        var convo = MakeConversation(clinic.Id, lead.Id, lastMessageAt: DateTimeOffset.UtcNow);
        var inbound = MakeMessage(convo, content: "hello world", createdAt: DateTimeOffset.UtcNow.AddHours(-1));
        var failed = MakeMessage(convo, MessageDirection.Outbound, MessageOrigin.Ai, "oops", DateTimeOffset.UtcNow.AddMinutes(-30));
        failed.SenderType = "ai"; failed.DeliveryStatus = "failed"; failed.FailedAt = DateTimeOffset.UtcNow; failed.FailureCode = "131026";
        var upcoming = MakeAppointment(clinic.Id, lead.Id, DateTimeOffset.UtcNow.AddDays(2), procedureId: proc.Id);
        var wa = new ChannelIntegration
        {
            Id = Guid.NewGuid(), ClinicId = clinic.Id, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected,
            IsHealthy = false, LastProblemMessage = "token expired", CreatedAt = Now, UpdatedAt = Now,
        };
        await SeedAsync(clinic, proc, lead, convo, inbound, failed, upcoming, wa);
        return new World(clinic, proc, lead, convo, inbound, failed, upcoming, wa);
    }

    // ---- clinics ----------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Clinics_list_search_filter_and_count()
    {
        var w = await SeedWorldAsync("Zeta Clinic " + Guid.NewGuid().ToString("N")[..6]);
        var inactive = MakeClinic("Zeta Closed " + Guid.NewGuid().ToString("N")[..6]); inactive.IsActive = false;
        await SeedAsync(inactive);

        await using var db = NewContext();
        var repo = new ClinicAdminRepository(db);
        var prefix = "Zeta ";
        var all = await repo.ListAsync(prefix, null, 1);
        Assert.Equal(2, all.Total);
        var row = all.Items.Single(r => r.Id == w.Clinic.Id);
        Assert.Equal((1, 1, ChannelIntegrationStatus.Connected), (row.LeadCount, row.ConversationCount, row.WhatsAppStatus));
        Assert.Equal("not set up", all.Items.Single(r => r.Id == inactive.Id).WhatsAppStatus);

        Assert.Equal(w.Clinic.Id, Assert.Single((await repo.ListAsync(prefix, true, 1)).Items).Id);
        Assert.Equal(inactive.Id, Assert.Single((await repo.ListAsync(prefix, false, 1)).Items).Id);
        Assert.Equal(w.Clinic.Id, Assert.Single((await repo.ListAsync(w.Clinic.Slug, null, 1)).Items).Id);          // search matches slug
        Assert.Equal(w.Clinic.Id, Assert.Single((await repo.ListAsync(w.Clinic.Email!, null, 1)).Items).Id);        // …and e-mail

        Assert.Contains(await repo.OptionsAsync(), o => o.Id == w.Clinic.Id);
        Assert.NotNull(await repo.GetAsync(w.Clinic.Id));
        Assert.Null(await repo.GetAsync(Guid.NewGuid()));
        Assert.NotNull(await repo.GetForUpdateAsync(w.Clinic.Id));

        var counts = await repo.CountsAsync(w.Clinic.Id);
        Assert.Equal((1, 1, 2, 1, 1), (counts.Leads, counts.Conversations, counts.Messages, counts.UpcomingAppointments, counts.Procedures));
        Assert.Equal(1, counts.FailedMessagesLast7Days);
    }

    // ---- channels, calendars, tiktok ---------------------------------------------------------------------------

    [PostgresFact]
    public async Task Channels_calendars_and_tiktok_are_listed_per_clinic_with_problem_filter()
    {
        var w = await SeedWorldAsync();
        var healthy = new ChannelIntegration
        {
            Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected, CreatedAt = Now, UpdatedAt = Now,
        };
        var calendar = new CalendarIntegration { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, Provider = "google", Status = CalendarIntegrationStatus.Connected, CreatedAt = Now, UpdatedAt = Now };
        var tiktok = new TikTokIntegration { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, CreatedAt = Now, UpdatedAt = Now };
        var health = new WhatsAppHealthEvent { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, ChannelIntegrationId = w.WhatsApp.Id, EventType = "FLAGGED", OccurredAt = Now, CreatedAt = Now };
        await SeedAsync(healthy, calendar, tiktok, health);

        await using var db = NewContext();
        var repo = new ChannelAdminRepository(db);
        Assert.Equal(2, (await repo.ListChannelsAsync(w.Clinic.Id, null, false)).Count);
        Assert.Equal(w.WhatsApp.Id, Assert.Single(await repo.ListChannelsAsync(w.Clinic.Id, ChannelType.WhatsApp, false)).Id);
        Assert.Equal(w.WhatsApp.Id, Assert.Single(await repo.ListChannelsAsync(w.Clinic.Id, null, true)).Id);      // problems only
        Assert.NotNull(await repo.GetChannelAsync(w.WhatsApp.Id));
        Assert.Equal(health.Id, Assert.Single(await repo.HealthEventsAsync(w.WhatsApp.Id)).Id);

        Assert.Equal(calendar.Id, Assert.Single(await repo.ListCalendarsAsync(w.Clinic.Id)).Id);
        Assert.Contains(await repo.ListCalendarsAsync(null), c => c.Id == calendar.Id);
        Assert.NotNull(await repo.GetCalendarAsync(calendar.Id));
        Assert.Equal(tiktok.Id, Assert.Single(await repo.ListTikTokAsync(w.Clinic.Id)).Id);
        Assert.NotNull(await repo.GetTikTokAsync(tiktok.Id));
    }

    // ---- staff ------------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Staff_are_listed_with_membership_and_name()
    {
        var w = await SeedWorldAsync();
        var email = $"staff-{Guid.NewGuid():N}@x.com";
        var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        var noMembership = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = "lonely-" + email, NormalizedUserName = ("LONELY-" + email).ToUpperInvariant(), Email = "lonely-" + email, NormalizedEmail = ("LONELY-" + email).ToUpperInvariant() };
        var membership = new ClinicUser { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, UserId = user.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(user, noMembership, membership);
        await SeedAsync(new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "full_name", ClaimValue = "Sam Staff" });

        await using var db = NewContext();
        var repo = new StaffAdminRepository(db);
        var byClinic = await repo.ListAsync(w.Clinic.Id, null, 1);
        var row = Assert.Single(byClinic.Items);
        Assert.Equal((user.Id, "Sam Staff", true), (row.UserId, row.FullName, row.MembershipActive));
        Assert.Contains(await repo.ListAsync(null, email, 1) is { } found ? found.Items : [], r => r.UserId == user.Id);
        Assert.Contains((await repo.ListAsync(null, "lonely-" + email, 1)).Items, r => r.UserId == noMembership.Id);

        Assert.Equal(w.Clinic.Id, (await repo.GetAsync(user.Id))!.ClinicId);
        Assert.Null(await repo.GetAsync("nobody"));
        Assert.Equal(w.Clinic.Id, await repo.ClinicOfAsync(user.Id));
        Assert.Null(await repo.ClinicOfAsync(noMembership.Id));
        Assert.Equal(membership.Id, (await repo.GetLatestMembershipAsync(user.Id))!.Id);
        Assert.False(await repo.HasOtherActiveMembershipAsync(user.Id, membership.Id));
        Assert.True(await repo.HasOtherActiveMembershipAsync(user.Id, Guid.NewGuid()));
    }

    // ---- leads, conversations, messages, appointments -----------------------------------------------------------

    [PostgresFact]
    public async Task Leads_conversations_messages_and_appointments_are_listed_and_filtered()
    {
        var w = await SeedWorldAsync();
        var evt = new EventLog { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, LeadId = w.Lead.Id, EventType = EventTypes.ProcedureBooked, CreatedAt = Now };
        await SeedAsync(evt);

        await using var db = NewContext();
        var repo = new LeadAdminRepository(db);

        var leads = await repo.ListLeadsAsync(w.Clinic.Id, null, null, 1);
        Assert.Equal((w.Lead.Id, "Rhinoplasty"), (leads.Items[0].Id, leads.Items[0].ProcedureName));
        Assert.Single((await repo.ListLeadsAsync(w.Clinic.Id, LeadStatus.New, "alice", 1)).Items);
        Assert.Empty((await repo.ListLeadsAsync(w.Clinic.Id, LeadStatus.Lost, null, 1)).Items);
        Assert.Empty((await repo.ListLeadsAsync(w.Clinic.Id, null, "zzz", 1)).Items);
        Assert.NotNull(await repo.GetLeadAsync(w.Lead.Id));
        Assert.Equal(evt.Id, Assert.Single(await repo.LeadEventsAsync(w.Lead.Id)).Id);

        var conversations = await repo.ListConversationsAsync(w.Clinic.Id, null, null, null, null, 1);
        Assert.Equal(2, Assert.Single(conversations.Items).MessageCount);
        Assert.Single((await repo.ListConversationsAsync(null, w.Lead.Id, "whatsapp", ConversationMode.Ai, ConversationStatus.Active, 1)).Items);
        Assert.Empty((await repo.ListConversationsAsync(w.Clinic.Id, null, "telegram", null, null, 1)).Items);
        Assert.NotNull(await repo.GetConversationAsync(w.Conversation.Id));
        Assert.Equal(["hello world", "oops"], (await repo.MessagesAsync(w.Conversation.Id)).Select(m => m.Content));
        Assert.Single(await repo.MessagesAsync(w.Conversation.Id, take: 1));

        Assert.Equal(2, (await repo.ListMessagesAsync(w.Clinic.Id, false, null, null, 1)).Total);
        Assert.Equal(w.Failed.Id, Assert.Single((await repo.ListMessagesAsync(w.Clinic.Id, true, null, null, 1)).Items).Id);
        Assert.Equal(w.Failed.Id, Assert.Single((await repo.ListMessagesAsync(w.Clinic.Id, false, "ai", null, 1)).Items).Id);
        Assert.Equal(w.Inbound.Id, Assert.Single((await repo.ListMessagesAsync(w.Clinic.Id, false, null, "HELLO", 1)).Items).Id);

        Assert.Equal(w.Upcoming.Id, Assert.Single((await repo.ListAppointmentsAsync(w.Clinic.Id, null, null, false, 1)).Items).Id);
        Assert.Single((await repo.ListAppointmentsAsync(w.Clinic.Id, w.Lead.Id, AppointmentStatus.Booked, true, 1)).Items);
        Assert.Empty((await repo.ListAppointmentsAsync(w.Clinic.Id, null, AppointmentStatus.Canceled, false, 1)).Items);

        Assert.NotNull(await repo.GetLeadForUpdateAsync(w.Lead.Id));
        Assert.NotNull(await repo.GetConversationForUpdateAsync(w.Conversation.Id));
        Assert.NotNull(await repo.GetAppointmentForUpdateAsync(w.Upcoming.Id));
    }

    // ---- campaigns, templates, procedures, knowledge ----------------------------------------------------------------

    [PostgresFact]
    public async Task Campaigns_templates_procedures_knowledge_and_websites_are_listed()
    {
        var w = await SeedWorldAsync();
        var template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, Name = "promo", Body = "b", Status = WhatsAppTemplateStatus.Approved, CreatedAt = Now, UpdatedAt = Now };
        var campaign = new Campaign { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, Name = "Spring", WhatsAppTemplateId = template.Id, Status = CampaignStatus.Completed, CreatedAt = Now, UpdatedAt = Now };
        var doc = new KnowledgeDocument { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, Title = "Pricing", Content = "abc", CreatedAt = Now, UpdatedAt = Now };
        var settings = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, EmbeddingModel = "m", VectorDimension = 1536, ChunkSizeTokens = 500, ChunkOverlapTokens = 50,
            TopK = 5, MinimumSimilarity = 0.3, CreatedAt = Now, UpdatedAt = Now,
        };
        var source = new KnowledgeWebsiteSource { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, StartUrl = "https://x.example/", NormalizedStartUrl = "https://x.example/", Host = "x.example", CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(template, doc, settings, source);
        await SeedAsync(campaign);
        var l2 = MakeLead(w.Clinic.Id, "Bob", "+9612");
        await SeedAsync(l2);
        var recipient = new CampaignRecipient
        {
            Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, CampaignId = campaign.Id, LeadId = w.Lead.Id, PhoneNumber = "+9611",
            Status = CampaignRecipientStatus.Sent, SentAt = Now, CreatedAt = Now, UpdatedAt = Now,
        };
        var queued = new CampaignRecipient
        {
            Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, CampaignId = campaign.Id, LeadId = l2.Id, PhoneNumber = "+9612",
            Status = CampaignRecipientStatus.Queued, CreatedAt = Now, UpdatedAt = Now,
        };
        await SeedAsync(recipient, queued);

        await using var db = NewContext();
        var repo = new ContentAdminRepository(db);

        var campaigns = await repo.ListCampaignsAsync(w.Clinic.Id, null, 1);
        var row = Assert.Single(campaigns.Items);
        Assert.Equal(("Spring", "promo", 2, 1), (row.Name, row.TemplateName, row.Recipients, row.Sent));
        Assert.Single((await repo.ListCampaignsAsync(w.Clinic.Id, CampaignStatus.Completed, 1)).Items);
        Assert.Empty((await repo.ListCampaignsAsync(w.Clinic.Id, CampaignStatus.Draft, 1)).Items);
        Assert.Equal(campaign.Id, (await repo.GetCampaignAsync(campaign.Id))!.Id);
        Assert.Null(await repo.GetCampaignAsync(Guid.NewGuid()));
        Assert.Equal(2, (await repo.RecipientsAsync(campaign.Id, null, 1)).Total);
        Assert.Equal(recipient.Id, Assert.Single((await repo.RecipientsAsync(campaign.Id, CampaignRecipientStatus.Sent, 1)).Items).Id);

        Assert.Equal(template.Id, Assert.Single(await repo.ListTemplatesAsync(w.Clinic.Id, null)).Id);
        Assert.Single(await repo.ListTemplatesAsync(w.Clinic.Id, WhatsAppTemplateStatus.Approved));
        Assert.Empty(await repo.ListTemplatesAsync(w.Clinic.Id, WhatsAppTemplateStatus.Rejected));
        Assert.Equal(2, (await repo.ListProceduresAsync(w.Clinic.Id) is var procs ? procs.Count + 1 : 0));   // one procedure…
        Assert.Equal(w.Procedure.Id, (await repo.GetProcedureAsync(w.Procedure.Id))!.Id);

        Assert.Equal(doc.Id, Assert.Single((await repo.ListDocumentsAsync(w.Clinic.Id, null, null, 1)).Items).Id);
        Assert.Single((await repo.ListDocumentsAsync(w.Clinic.Id, "pric", true, 1)).Items);
        Assert.Empty((await repo.ListDocumentsAsync(w.Clinic.Id, null, false, 1)).Items);
        Assert.NotNull(await repo.GetDocumentAsync(doc.Id));
        Assert.Equal(5, (await repo.SearchSettingsAsync(w.Clinic.Id))!.TopK);
        Assert.Null(await repo.SearchSettingsAsync(Guid.NewGuid()));
        Assert.Equal(source.Id, Assert.Single(await repo.ListWebsitesAsync(w.Clinic.Id)).Id);
    }

    // ---- overview -----------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Overview_totals_daily_counts_problems_and_events()
    {
        var w = await SeedWorldAsync("Overview " + Guid.NewGuid().ToString("N")[..6]);
        var evt = new EventLog { Id = Guid.NewGuid(), ClinicId = w.Clinic.Id, EventType = "zz_test_event_" + Guid.NewGuid().ToString("N")[..6], CreatedAt = Now };
        await SeedAsync(evt);

        await using var db = NewContext();
        var repo = new OverviewAdminRepository(db);

        var totals = await repo.TotalsAsync();
        Assert.True(totals.Clinics >= 1 && totals.Leads >= 1 && totals.Conversations >= 1);

        var daily = await repo.DailyMessagesAsync(3);
        Assert.NotEmpty(daily);
        Assert.True(daily.Sum(d => d.Inbound) >= 1);
        Assert.True(daily.Sum(d => d.Failed) >= 1);

        var problems = await repo.ProblemsAsync();
        Assert.Contains(problems, p => p.ClinicId == w.Clinic.Id);                   // the unhealthy WhatsApp channel and/or failed message

        Assert.Contains(await repo.RecentClinicsAsync(500), c => c.Id == w.Clinic.Id);
        Assert.Single((await repo.EventsAsync(w.Clinic.Id, null, 1)).Items);
        Assert.Single((await repo.EventsAsync(w.Clinic.Id, evt.EventType, 1)).Items);
        Assert.Empty((await repo.EventsAsync(w.Clinic.Id, "other_type", 1)).Items);
        Assert.Contains(evt.EventType, await repo.EventTypesAsync());
    }
}

public class UnitOfWorkTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task A_unique_violation_becomes_a_duplicate_record_exception()
    {
        var c = MakeClinic();
        await SeedAsync(c);
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        db.Clinics.Add(new Clinic { Id = Guid.NewGuid(), Name = "dup", Slug = c.Slug, CreatedAt = Now, UpdatedAt = Now });
        var ex = await Assert.ThrowsAsync<DuplicateRecordException>(() => uow.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.True(uow.IsDuplicateRecord(ex));
    }

    [PostgresFact]
    public async Task Try_save_reports_false_and_clears_the_tracker_on_a_duplicate()
    {
        var c = MakeClinic();
        await SeedAsync(c);
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        db.Clinics.Add(new Clinic { Id = Guid.NewGuid(), Name = "dup", Slug = c.Slug, CreatedAt = Now, UpdatedAt = Now });
        Assert.False(await uow.TrySaveChangesAsync());
        Assert.Empty(db.ChangeTracker.Entries());

        db.Clinics.Add(MakeClinic());
        Assert.True(await uow.TrySaveChangesAsync());
    }

    [PostgresFact]
    public async Task Save_returns_the_row_count_and_other_database_errors_are_not_duplicates()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        db.Clinics.Add(MakeClinic());
        Assert.Equal(1, await uow.SaveChangesAsync());
        Assert.False(uow.IsDuplicateRecord(new InvalidOperationException()));
        Assert.True(uow.IsDuplicateRecord(new DuplicateRecordException(new Exception())));
    }

    [PostgresFact]
    public async Task Discard_forgets_pending_changes_and_transactions_commit_or_roll_back()
    {
        var committed = MakeClinic(); var rolledBack = MakeClinic();
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        db.Clinics.Add(MakeClinic());
        uow.DiscardChanges();
        Assert.Empty(db.ChangeTracker.Entries());

        await using (var tx = await uow.BeginTransactionAsync())
        {
            db.Clinics.Add(committed);
            await uow.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using (var tx = await uow.BeginTransactionAsync())
        {
            db.Clinics.Add(rolledBack);
            await uow.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        await using var check = NewContext();
        Assert.True(await check.Clinics.AnyAsync(x => x.Id == committed.Id));
        Assert.False(await check.Clinics.AnyAsync(x => x.Id == rolledBack.Id));
    }
}
