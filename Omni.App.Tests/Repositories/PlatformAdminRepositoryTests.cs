using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.PlatformAdmin;

namespace Omni.App.Tests.Repositories;

/// <summary>
/// The cross-client reads behind /api/platform-admin. Every test seeds its own client and filters by it, so rows from other
/// tests in the shared database never leak in; only the global totals are asserted as lower bounds.
/// </summary>
public class PlatformAdminRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private sealed record World(Client Client, Service Service, Contact Contact, Conversation Conversation, Message Inbound, Message Failed,
        Booking Upcoming, ChannelIntegration WhatsApp);

    private async Task<World> SeedWorldAsync(string? clientName = null)
    {
        var client = MakeClient(clientName);
        client.Email = "owner@" + client.Slug + ".test";
        var svc = MakeService(client.Id, "Rhinoplasty");
        var contact = MakeContact(client.Id, "Alice Wong", "+9611", svc.Id);
        var convo = MakeConversation(client.Id, contact.Id, lastMessageAt: DateTimeOffset.UtcNow);
        var inbound = MakeMessage(convo, content: "hello world", createdAt: DateTimeOffset.UtcNow.AddHours(-1));
        var failed = MakeMessage(convo, MessageDirection.Outbound, MessageOrigin.Ai, "oops", DateTimeOffset.UtcNow.AddMinutes(-30));
        failed.SenderType = "ai"; failed.DeliveryStatus = "failed"; failed.FailedAt = DateTimeOffset.UtcNow; failed.FailureCode = "131026";
        var upcoming = MakeBooking(client.Id, contact.Id, DateTimeOffset.UtcNow.AddDays(2), serviceId: svc.Id);
        var wa = new ChannelIntegration
        {
            Id = Guid.NewGuid(), ClientId = client.Id, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected,
            IsHealthy = false, LastProblemMessage = "token expired", CreatedAt = Now, UpdatedAt = Now,
        };
        await SeedAsync(client, svc, contact, convo, inbound, failed, upcoming, wa);
        return new World(client, svc, contact, convo, inbound, failed, upcoming, wa);
    }

    // ---- clients ----------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Clients_list_search_filter_and_count()
    {
        var w = await SeedWorldAsync("Zeta Client " + Guid.NewGuid().ToString("N")[..6]);
        var inactive = MakeClient("Zeta Closed " + Guid.NewGuid().ToString("N")[..6]); inactive.IsActive = false;
        await SeedAsync(inactive);

        await using var db = NewContext();
        var repo = new ClientAdminRepository(db);
        var prefix = "Zeta ";
        var all = await repo.ListAsync(prefix, null, 1);
        Assert.Equal(2, all.Total);
        var row = all.Items.Single(r => r.Id == w.Client.Id);
        Assert.Equal((1, 1, ChannelIntegrationStatus.Connected), (row.ContactCount, row.ConversationCount, row.WhatsAppStatus));
        Assert.Equal("not set up", all.Items.Single(r => r.Id == inactive.Id).WhatsAppStatus);

        Assert.Equal(w.Client.Id, Assert.Single((await repo.ListAsync(prefix, true, 1)).Items).Id);
        Assert.Equal(inactive.Id, Assert.Single((await repo.ListAsync(prefix, false, 1)).Items).Id);
        Assert.Equal(w.Client.Id, Assert.Single((await repo.ListAsync(w.Client.Slug, null, 1)).Items).Id);          // search matches slug
        Assert.Equal(w.Client.Id, Assert.Single((await repo.ListAsync(w.Client.Email!, null, 1)).Items).Id);        // …and e-mail

        Assert.Contains(await repo.OptionsAsync(), o => o.Id == w.Client.Id);
        Assert.NotNull(await repo.GetAsync(w.Client.Id));
        Assert.Null(await repo.GetAsync(Guid.NewGuid()));
        Assert.NotNull(await repo.GetForUpdateAsync(w.Client.Id));

        var counts = await repo.CountsAsync(w.Client.Id);
        Assert.Equal((1, 1, 2, 1, 1), (counts.Contacts, counts.Conversations, counts.Messages, counts.UpcomingBookings, counts.Services));
        Assert.Equal(1, counts.FailedMessagesLast7Days);
    }

    // ---- channels, calendars, tiktok ---------------------------------------------------------------------------

    [PostgresFact]
    public async Task Channels_calendars_and_tiktok_are_listed_per_client_with_problem_filter()
    {
        var w = await SeedWorldAsync();
        var healthy = new ChannelIntegration
        {
            Id = Guid.NewGuid(), ClientId = w.Client.Id, Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected, CreatedAt = Now, UpdatedAt = Now,
        };
        var calendar = new CalendarIntegration { Id = Guid.NewGuid(), ClientId = w.Client.Id, Provider = "google", Status = CalendarIntegrationStatus.Connected, CreatedAt = Now, UpdatedAt = Now };
        var tiktok = new TikTokIntegration { Id = Guid.NewGuid(), ClientId = w.Client.Id, CreatedAt = Now, UpdatedAt = Now };
        var health = new WhatsAppHealthEvent { Id = Guid.NewGuid(), ClientId = w.Client.Id, ChannelIntegrationId = w.WhatsApp.Id, EventType = "FLAGGED", OccurredAt = Now, CreatedAt = Now };
        await SeedAsync(healthy, calendar, tiktok, health);

        await using var db = NewContext();
        var repo = new ChannelAdminRepository(db);
        Assert.Equal(2, (await repo.ListChannelsAsync(w.Client.Id, null, false)).Count);
        Assert.Equal(w.WhatsApp.Id, Assert.Single(await repo.ListChannelsAsync(w.Client.Id, ChannelType.WhatsApp, false)).Id);
        Assert.Equal(w.WhatsApp.Id, Assert.Single(await repo.ListChannelsAsync(w.Client.Id, null, true)).Id);      // problems only
        Assert.NotNull(await repo.GetChannelAsync(w.WhatsApp.Id));
        Assert.Equal(health.Id, Assert.Single(await repo.HealthEventsAsync(w.WhatsApp.Id)).Id);

        Assert.Equal(calendar.Id, Assert.Single(await repo.ListCalendarsAsync(w.Client.Id)).Id);
        Assert.Contains(await repo.ListCalendarsAsync(null), c => c.Id == calendar.Id);
        Assert.NotNull(await repo.GetCalendarAsync(calendar.Id));
        Assert.Equal(tiktok.Id, Assert.Single(await repo.ListTikTokAsync(w.Client.Id)).Id);
        Assert.NotNull(await repo.GetTikTokAsync(tiktok.Id));
    }

    // ---- members-----------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Members_are_listed_with_membership_and_name_and_admins_are_not()
    {
        var w = await SeedWorldAsync();
        var email = $"member-{Guid.NewGuid():N}@x.com";
        var user = MakeUser(email, UserRole.Member, "Sam Member");
        var noMembership = MakeUser("lonely-" + email);
        var admin = MakeUser("admin-" + email, UserRole.Admin, "Ada Admin");
        var membership = new ClientUser { Id = Guid.NewGuid(), ClientId = w.Client.Id, UserId = user.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(user, noMembership, admin, membership);

        await using var db = NewContext();
        var repo = new MemberAdminRepository(db);
        var byClient = await repo.ListAsync(w.Client.Id, null, 1);
        var row = Assert.Single(byClient.Items);
        Assert.Equal((user.Id, "Sam Member", true, UserRole.Member, true), (row.UserId, row.FullName, row.MembershipActive, row.Role, row.UserActive));
        Assert.Contains(await repo.ListAsync(null, email, 1) is { } found ? found.Items : [], r => r.UserId == user.Id);
        Assert.Contains((await repo.ListAsync(null, "Sam Mem", 1)).Items, r => r.UserId == user.Id);  // by name too
        Assert.Contains((await repo.ListAsync(null, "lonely-" + email, 1)).Items, r => r.UserId == noMembership.Id);
        Assert.Empty((await repo.ListAsync(null, "admin-" + email, 1)).Items);                         // admins live under /admins

        Assert.Equal(w.Client.Id, (await repo.GetAsync(user.Id))!.ClientId);
        Assert.Null(await repo.GetAsync("nobody"));
        Assert.Null(await repo.GetAsync(admin.Id));
        Assert.Equal(w.Client.Id, await repo.ClientOfAsync(user.Id));
        Assert.Null(await repo.ClientOfAsync(noMembership.Id));
        Assert.Equal(membership.Id, (await repo.GetLatestMembershipAsync(user.Id))!.Id);
    }

    [PostgresFact]
    public async Task Admins_are_listed_and_counted_apart_from_client_users()
    {
        var active = MakeUser($"a-{Guid.NewGuid():N}@x.com", UserRole.Admin, "Ada");
        var off = MakeUser($"b-{Guid.NewGuid():N}@x.com", UserRole.Admin, "Bo", active: false);
        var client = MakeUser();
        await SeedAsync(active, off, client);

        await using var db = NewContext();
        var repo = new AdminUserRepository(db);
        var before = await repo.CountActiveAsync();
        var list = await repo.ListAsync();
        Assert.Contains(list, a => a.Id == active.Id && a.IsActive && a.FullName == "Ada");
        Assert.Contains(list, a => a.Id == off.Id && !a.IsActive);
        Assert.DoesNotContain(list, a => a.Id == client.Id);
        var ids = list.Select(a => a.Id).ToList();
        Assert.True(ids.IndexOf(active.Id) < ids.IndexOf(off.Id)); // active first
        Assert.Equal(list.Count(a => a.IsActive), before);
    }

    // ---- contacts, conversations, messages, bookings -----------------------------------------------------------

    [PostgresFact]
    public async Task Contacts_conversations_messages_and_bookings_are_listed_and_filtered()
    {
        var w = await SeedWorldAsync();
        var evt = new EventLog { Id = Guid.NewGuid(), ClientId = w.Client.Id, ContactId = w.Contact.Id, EventType = EventTypes.OrderConfirmed, CreatedAt = Now };
        await SeedAsync(evt);

        await using var db = NewContext();
        var repo = new ContactAdminRepository(db);

        var contacts = await repo.ListContactsAsync(w.Client.Id, null, null, 1);
        Assert.Equal((w.Contact.Id, "Rhinoplasty"), (contacts.Items[0].Id, contacts.Items[0].ServiceName));
        Assert.Single((await repo.ListContactsAsync(w.Client.Id, PipelineKit.New, "alice", 1)).Items);
        Assert.Empty((await repo.ListContactsAsync(w.Client.Id, PipelineKit.Lost, null, 1)).Items);
        Assert.Empty((await repo.ListContactsAsync(w.Client.Id, null, "zzz", 1)).Items);
        Assert.NotNull(await repo.GetContactAsync(w.Contact.Id));
        Assert.Equal(evt.Id, Assert.Single(await repo.ContactEventsAsync(w.Contact.Id)).Id);

        var conversations = await repo.ListConversationsAsync(w.Client.Id, null, null, null, null, 1);
        Assert.Equal(2, Assert.Single(conversations.Items).MessageCount);
        Assert.Single((await repo.ListConversationsAsync(null, w.Contact.Id, "whatsapp", ConversationMode.Ai, ConversationStatus.Active, 1)).Items);
        Assert.Empty((await repo.ListConversationsAsync(w.Client.Id, null, "telegram", null, null, 1)).Items);
        Assert.NotNull(await repo.GetConversationAsync(w.Conversation.Id));
        Assert.Equal(["hello world", "oops"], (await repo.MessagesAsync(w.Conversation.Id)).Select(m => m.Content));
        Assert.Single(await repo.MessagesAsync(w.Conversation.Id, take: 1));

        Assert.Equal(2, (await repo.ListMessagesAsync(w.Client.Id, false, null, null, 1)).Total);
        Assert.Equal(w.Failed.Id, Assert.Single((await repo.ListMessagesAsync(w.Client.Id, true, null, null, 1)).Items).Id);
        Assert.Equal(w.Failed.Id, Assert.Single((await repo.ListMessagesAsync(w.Client.Id, false, "ai", null, 1)).Items).Id);
        Assert.Equal(w.Inbound.Id, Assert.Single((await repo.ListMessagesAsync(w.Client.Id, false, null, "HELLO", 1)).Items).Id);

        Assert.Equal(w.Upcoming.Id, Assert.Single((await repo.ListBookingsAsync(w.Client.Id, null, null, false, 1)).Items).Id);
        Assert.Single((await repo.ListBookingsAsync(w.Client.Id, w.Contact.Id, BookingStatus.Booked, true, 1)).Items);
        Assert.Empty((await repo.ListBookingsAsync(w.Client.Id, null, BookingStatus.Canceled, false, 1)).Items);

        Assert.NotNull(await repo.GetContactForUpdateAsync(w.Contact.Id));
        Assert.NotNull(await repo.GetConversationForUpdateAsync(w.Conversation.Id));
        Assert.NotNull(await repo.GetBookingForUpdateAsync(w.Upcoming.Id));
    }

    // ---- campaigns, templates, services, knowledge ----------------------------------------------------------------

    [PostgresFact]
    public async Task Campaigns_templates_services_knowledge_and_websites_are_listed()
    {
        var w = await SeedWorldAsync();
        var template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = w.Client.Id, Name = "promo", Body = "b", Status = WhatsAppTemplateStatus.Approved, CreatedAt = Now, UpdatedAt = Now };
        var campaign = new Campaign { Id = Guid.NewGuid(), ClientId = w.Client.Id, Name = "Spring", WhatsAppTemplateId = template.Id, Status = CampaignStatus.Completed, CreatedAt = Now, UpdatedAt = Now };
        var doc = new KnowledgeDocument { Id = Guid.NewGuid(), ClientId = w.Client.Id, Title = "Pricing", Content = "abc", CreatedAt = Now, UpdatedAt = Now };
        var settings = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(), ClientId = w.Client.Id, EmbeddingModel = "m", VectorDimension = 1536, ChunkSizeTokens = 500, ChunkOverlapTokens = 50,
            TopK = 5, MinimumSimilarity = 0.3, CreatedAt = Now, UpdatedAt = Now,
        };
        var source = new KnowledgeWebsiteSource { Id = Guid.NewGuid(), ClientId = w.Client.Id, StartUrl = "https://x.example/", NormalizedStartUrl = "https://x.example/", Host = "x.example", CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(template, doc, settings, source);
        await SeedAsync(campaign);
        var l2 = MakeContact(w.Client.Id, "Bob", "+9612");
        await SeedAsync(l2);
        var recipient = new CampaignRecipient
        {
            Id = Guid.NewGuid(), ClientId = w.Client.Id, CampaignId = campaign.Id, ContactId = w.Contact.Id, PhoneNumber = "+9611",
            Status = CampaignRecipientStatus.Sent, SentAt = Now, CreatedAt = Now, UpdatedAt = Now,
        };
        var queued = new CampaignRecipient
        {
            Id = Guid.NewGuid(), ClientId = w.Client.Id, CampaignId = campaign.Id, ContactId = l2.Id, PhoneNumber = "+9612",
            Status = CampaignRecipientStatus.Queued, CreatedAt = Now, UpdatedAt = Now,
        };
        await SeedAsync(recipient, queued);

        await using var db = NewContext();
        var repo = new ContentAdminRepository(db);

        var campaigns = await repo.ListCampaignsAsync(w.Client.Id, null, 1);
        var row = Assert.Single(campaigns.Items);
        Assert.Equal(("Spring", "promo", 2, 1), (row.Name, row.TemplateName, row.Recipients, row.Sent));
        Assert.Single((await repo.ListCampaignsAsync(w.Client.Id, CampaignStatus.Completed, 1)).Items);
        Assert.Empty((await repo.ListCampaignsAsync(w.Client.Id, CampaignStatus.Draft, 1)).Items);
        Assert.Equal(campaign.Id, (await repo.GetCampaignAsync(campaign.Id))!.Id);
        Assert.Null(await repo.GetCampaignAsync(Guid.NewGuid()));
        Assert.Equal(2, (await repo.RecipientsAsync(campaign.Id, null, 1)).Total);
        Assert.Equal(recipient.Id, Assert.Single((await repo.RecipientsAsync(campaign.Id, CampaignRecipientStatus.Sent, 1)).Items).Id);

        Assert.Equal(template.Id, Assert.Single(await repo.ListTemplatesAsync(w.Client.Id, null)).Id);
        Assert.Single(await repo.ListTemplatesAsync(w.Client.Id, WhatsAppTemplateStatus.Approved));
        Assert.Empty(await repo.ListTemplatesAsync(w.Client.Id, WhatsAppTemplateStatus.Rejected));
        Assert.Equal(2, (await repo.ListServicesAsync(w.Client.Id) is var procs ? procs.Count + 1 : 0));   // one service…
        Assert.Equal(w.Service.Id, (await repo.GetServiceAsync(w.Service.Id))!.Id);

        Assert.Equal(doc.Id, Assert.Single((await repo.ListDocumentsAsync(w.Client.Id, null, null, 1)).Items).Id);
        Assert.Single((await repo.ListDocumentsAsync(w.Client.Id, "pric", true, 1)).Items);
        Assert.Empty((await repo.ListDocumentsAsync(w.Client.Id, null, false, 1)).Items);
        Assert.NotNull(await repo.GetDocumentAsync(doc.Id));
        Assert.Equal(5, (await repo.SearchSettingsAsync(w.Client.Id))!.TopK);
        Assert.Null(await repo.SearchSettingsAsync(Guid.NewGuid()));
        Assert.Equal(source.Id, Assert.Single(await repo.ListWebsitesAsync(w.Client.Id)).Id);
    }

    // ---- overview -----------------------------------------------------------------------------------------------

    [PostgresFact]
    public async Task Overview_totals_daily_counts_problems_and_events()
    {
        var w = await SeedWorldAsync("Overview " + Guid.NewGuid().ToString("N")[..6]);
        var evt = new EventLog { Id = Guid.NewGuid(), ClientId = w.Client.Id, EventType = "zz_test_event_" + Guid.NewGuid().ToString("N")[..6], CreatedAt = Now };
        await SeedAsync(evt);

        await using var db = NewContext();
        var repo = new OverviewAdminRepository(db);

        var totals = await repo.TotalsAsync();
        Assert.True(totals.Clients >= 1 && totals.Contacts >= 1 && totals.Conversations >= 1);

        var daily = await repo.DailyMessagesAsync(3);
        Assert.NotEmpty(daily);
        Assert.True(daily.Sum(d => d.Inbound) >= 1);
        Assert.True(daily.Sum(d => d.Failed) >= 1);

        var problems = await repo.ProblemsAsync();
        Assert.Contains(problems, p => p.ClientId == w.Client.Id);                   // the unhealthy WhatsApp channel and/or failed message

        Assert.Contains(await repo.RecentClientsAsync(500), c => c.Id == w.Client.Id);
        Assert.Single((await repo.EventsAsync(w.Client.Id, null, 1)).Items);
        Assert.Single((await repo.EventsAsync(w.Client.Id, evt.EventType, 1)).Items);
        Assert.Empty((await repo.EventsAsync(w.Client.Id, "other_type", 1)).Items);
        Assert.Contains(evt.EventType, await repo.EventTypesAsync());
    }
}

public class UnitOfWorkTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task A_unique_violation_becomes_a_duplicate_record_exception()
    {
        var c = MakeClient();
        await SeedAsync(c);
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        db.Clients.Add(new Client { Id = Guid.NewGuid(), Name = "dup", Slug = c.Slug, BusinessTypeCode = TextKit.Clinic, CreatedAt = Now, UpdatedAt = Now });
        var ex = await Assert.ThrowsAsync<DuplicateRecordException>(() => uow.SaveChangesAsync());
        Assert.NotNull(ex);
        Assert.True(uow.IsDuplicateRecord(ex));
    }

    [PostgresFact]
    public async Task Try_save_reports_false_and_clears_the_tracker_on_a_duplicate()
    {
        var c = MakeClient();
        await SeedAsync(c);
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        db.Clients.Add(new Client { Id = Guid.NewGuid(), Name = "dup", Slug = c.Slug, BusinessTypeCode = TextKit.Clinic, CreatedAt = Now, UpdatedAt = Now });
        Assert.False(await uow.TrySaveChangesAsync());
        Assert.Empty(db.ChangeTracker.Entries());

        db.Clients.Add(MakeClient());
        Assert.True(await uow.TrySaveChangesAsync());
    }

    [PostgresFact]
    public async Task Save_returns_the_row_count_and_other_database_errors_are_not_duplicates()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);
        db.Clients.Add(MakeClient());
        Assert.Equal(1, await uow.SaveChangesAsync());
        Assert.False(uow.IsDuplicateRecord(new InvalidOperationException()));
        Assert.True(uow.IsDuplicateRecord(new DuplicateRecordException(new Exception())));
    }

    [PostgresFact]
    public async Task Discard_forgets_pending_changes_and_transactions_commit_or_roll_back()
    {
        var committed = MakeClient(); var rolledBack = MakeClient();
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        db.Clients.Add(MakeClient());
        uow.DiscardChanges();
        Assert.Empty(db.ChangeTracker.Entries());

        await using (var tx = await uow.BeginTransactionAsync())
        {
            db.Clients.Add(committed);
            await uow.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using (var tx = await uow.BeginTransactionAsync())
        {
            db.Clients.Add(rolledBack);
            await uow.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        await using var check = NewContext();
        Assert.True(await check.Clients.AnyAsync(x => x.Id == committed.Id));
        Assert.False(await check.Clients.AnyAsync(x => x.Id == rolledBack.Id));
    }
}
