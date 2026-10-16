using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Calendars;
using Omni.App.Persistence.Repositories.Campaigns;
using Omni.App.Persistence.Repositories.Channels;
using Omni.App.Persistence.Repositories.Configuration;
using Omni.App.Persistence.Repositories.Events;
using Omni.App.Persistence.Repositories.Knowledge;
using Omni.App.Persistence.Repositories.Notifications;
using Omni.App.Persistence.Repositories.Catalog;
using Omni.App.Persistence.Repositories.Orders;
using Omni.App.Persistence.Repositories.TikTok;
using Omni.App.Persistence.Repositories.Users;
using Omni.App.Persistence.Repositories.WhatsApp;

namespace Omni.App.Tests.Repositories;

public class ServiceRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Services_are_listed_by_name_and_filtered_to_active()
    {
        var c = MakeClient(); var other = MakeClient();
        var b = MakeService(c.Id, "Botox"); var a = MakeService(c.Id, "Abdominoplasty"); var off = MakeService(c.Id, "Chin", active: false);
        var foreign = MakeService(other.Id, "Foreign");
        await SeedAsync(c, other, b, a, off, foreign);

        await using var db = NewContext();
        var repo = new ServiceRepository(db);
        Assert.Equal(["Abdominoplasty", "Botox", "Chin"], (await repo.ListAsync(c.Id, false)).Select(p => p.Name));
        Assert.Equal(["Abdominoplasty", "Botox"], (await repo.ListAsync(c.Id, true)).Select(p => p.Name));
    }

    [PostgresFact]
    public async Task Lookups_activity_and_name_clashes()
    {
        var c = MakeClient(); var other = MakeClient();
        var p = MakeService(c.Id, "Rhinoplasty"); var off = MakeService(c.Id, "Retired", active: false);
        await SeedAsync(c, other, p, off);

        await using var db = NewContext();
        var repo = new ServiceRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, p.Id));
        Assert.Null(await repo.GetAsync(other.Id, p.Id));
        Assert.NotNull(await repo.GetReadOnlyAsync(c.Id, p.Id));
        Assert.Null(await repo.GetReadOnlyAsync(other.Id, p.Id));
        Assert.True(await repo.IsActiveAsync(p.Id));
        Assert.False(await repo.IsActiveAsync(off.Id));
        Assert.False(await repo.IsActiveAsync(Guid.NewGuid()));

        Assert.True(await repo.NameExistsAsync(c.Id, "RHINOPLASTY", null));
        Assert.False(await repo.NameExistsAsync(c.Id, "RHINOPLASTY", p.Id));   // renaming itself is fine
        Assert.False(await repo.NameExistsAsync(other.Id, "Rhinoplasty", null));

        var added = MakeService(c.Id, "Facelift");
        repo.Add(added);
        await db.SaveChangesAsync();
        await using var check = NewContext();
        Assert.True(await new ServiceRepository(check).NameExistsAsync(c.Id, "facelift", null));
    }
}

public class OrderRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Orders_are_scoped_filtered_ordered_and_loadable()
    {
        var c = MakeClient(); var other = MakeClient();
        var svc = MakeService(c.Id); var lead = MakeLead(c.Id);
        Order MakeOrder(string status, int daysAgo) => new()
        {
            Id = Guid.NewGuid(), ClientId = c.Id, LeadId = lead.Id, ServiceId = svc.Id, Status = status,
            CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now.AddDays(-daysAgo),
        };
        var older = MakeOrder(OrderStatus.Considering, 2);
        var newer = MakeOrder(OrderStatus.Confirmed, 1);
        await SeedAsync(c, other, svc, lead, older, newer);

        await using var db = NewContext();
        var repo = new OrderRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, older.Id));
        Assert.Null(await repo.GetAsync(other.Id, older.Id));

        var (items, total) = await repo.ListAsync(c.Id, null, 0, 10);
        Assert.Equal(2, total);
        Assert.Equal([newer.Id, older.Id], items.Select(b => b.Id));
        Assert.NotNull(items[0].Lead);
        Assert.NotNull(items[0].Service);
        Assert.Equal([newer.Id], (await repo.ListAsync(c.Id, OrderStatus.Confirmed, 0, 10)).Items.Select(b => b.Id));
        Assert.Equal([older.Id], (await repo.ListAsync(c.Id, null, 1, 10)).Items.Select(b => b.Id));

        var fresh = new Order { Id = Guid.NewGuid(), ClientId = c.Id, LeadId = lead.Id, ServiceId = svc.Id, CreatedAt = Now, UpdatedAt = Now };
        repo.Add(fresh);
        await db.SaveChangesAsync();
        await repo.LoadDetailsAsync(fresh);
        Assert.NotNull(fresh.Lead);
        Assert.NotNull(fresh.Service);
    }
}

public class NotificationRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static Notification Note(Guid clientId, bool read, int minutesAgo) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Type = NotificationType.AppointmentBooked, Title = "t" + minutesAgo, IsRead = read,
        CreatedAt = Now.AddMinutes(-minutesAgo),
    };

    [PostgresFact]
    public async Task List_counts_unread_and_pages_newest_first()
    {
        var c = MakeClient(); var other = MakeClient();
        var n1 = Note(c.Id, false, 3); var n2 = Note(c.Id, true, 2); var n3 = Note(c.Id, false, 1); var foreign = Note(other.Id, false, 1);
        await SeedAsync(c, other, n1, n2, n3, foreign);

        await using var db = NewContext();
        var repo = new NotificationRepository(db);
        var (items, total, unread) = await repo.ListAsync(c.Id, 0, 2);
        Assert.Equal((3, 2), (total, unread));
        Assert.Equal([n3.Id, n2.Id], items.Select(n => n.Id));
        Assert.Equal([n1.Id], (await repo.ListAsync(c.Id, 2, 2)).Items.Select(n => n.Id));
        Assert.Equal(2, await repo.CountUnreadAsync(c.Id));
        Assert.NotNull(await repo.GetAsync(c.Id, n1.Id));
        Assert.Null(await repo.GetAsync(other.Id, n1.Id));
    }

    [PostgresFact]
    public async Task Mark_all_read_only_touches_this_clients_unread_rows()
    {
        var c = MakeClient(); var other = MakeClient();
        await SeedAsync(c, other, Note(c.Id, false, 1), Note(c.Id, false, 2), Note(other.Id, false, 1));

        await using var db = NewContext();
        var repo = new NotificationRepository(db);
        await repo.MarkAllReadAsync(c.Id, Now);
        Assert.Equal(0, await repo.CountUnreadAsync(c.Id));
        Assert.Equal(1, await repo.CountUnreadAsync(other.Id));

        var added = Note(c.Id, false, 0);
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.Equal(1, await new NotificationRepository(NewContext()).CountUnreadAsync(c.Id));
    }
}

public class ChannelIntegrationRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static ChannelIntegration Integration(Guid clientId, string channel, string status = ChannelIntegrationStatus.Connected) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Channel = channel, Status = status, CreatedAt = Now, UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Lookups_by_client_channel_and_provider_identifiers()
    {
        var c = MakeClient(); var other = MakeClient();
        var phone = "P-" + Guid.NewGuid().ToString("N")[..8]; var waba = "W-" + Guid.NewGuid().ToString("N")[..8];
        var wa = Integration(c.Id, ChannelType.WhatsApp); wa.PhoneNumberId = phone; wa.WhatsAppBusinessId = waba;
        var tg = Integration(c.Id, ChannelType.Telegram);
        await SeedAsync(c, other, wa, tg);

        await using var db = NewContext();
        var repo = new ChannelIntegrationRepository(db);
        Assert.Equal(wa.Id, (await repo.GetAsync(c.Id, ChannelType.WhatsApp))!.Id);
        Assert.Null(await repo.GetAsync(other.Id, ChannelType.WhatsApp));
        Assert.NotNull(await repo.GetReadOnlyAsync(c.Id, ChannelType.Telegram));
        Assert.NotNull(await repo.GetByIdReadOnlyAsync(wa.Id));
        Assert.Equal(wa.Id, await repo.GetIdAsync(c.Id, ChannelType.WhatsApp));
        Assert.Equal(Guid.Empty, await repo.GetIdAsync(other.Id, ChannelType.WhatsApp));
        Assert.Equal(wa.Id, (await repo.FindWhatsAppByPhoneNumberIdAsync(phone))!.Id);
        Assert.Null(await repo.FindWhatsAppByPhoneNumberIdAsync("nope"));
        Assert.Equal(wa.Id, (await repo.FindWhatsAppByWabaIdAsync(waba))!.Id);
        Assert.Equal(2, (await repo.ListForClientReadOnlyAsync(c.Id)).Count);
        Assert.Equal([ChannelType.Telegram, ChannelType.WhatsApp], (await repo.ListForClientReadOnlyAsync(c.Id)).Select(i => i.Channel).Order());
    }

    [PostgresFact]
    public async Task Connected_elsewhere_checks_and_connection_counts()
    {
        var c = MakeClient(); var other = MakeClient();
        var bot = "bot-" + Guid.NewGuid().ToString("N")[..8];
        var tg = Integration(other.Id, ChannelType.Telegram); tg.TelegramBotId = bot;
        var wa = Integration(other.Id, ChannelType.WhatsApp);
        var mine = Integration(c.Id, ChannelType.Telegram);
        var down = Integration(c.Id, ChannelType.WhatsApp, ChannelIntegrationStatus.Disconnected);
        await SeedAsync(c, other, tg, wa, mine, down);

        await using var db = NewContext();
        var repo = new ChannelIntegrationRepository(db);
        Assert.True(await repo.IsTelegramBotConnectedElsewhereAsync(bot, c.Id));
        Assert.False(await repo.IsTelegramBotConnectedElsewhereAsync(bot, other.Id));
        Assert.Equal(1, await repo.CountConnectedExceptAsync(c.Id, ChannelType.WhatsApp));
        Assert.Equal(0, await repo.CountConnectedExceptAsync(c.Id, ChannelType.Telegram));
    }

    [PostgresFact]
    public async Task Touching_the_webhook_time_and_adding()
    {
        var c = MakeClient();
        var wa = Integration(c.Id, ChannelType.WhatsApp);
        await SeedAsync(c, wa);
        await using var db = NewContext();
        var repo = new ChannelIntegrationRepository(db);
        await repo.TouchLastWebhookAsync(wa.Id, Now);
        Assert.Equal(Now, (await repo.GetByIdReadOnlyAsync(wa.Id))!.LastWebhookAt);

        repo.Add(Integration(c.Id, ChannelType.Telegram));
        await db.SaveChangesAsync();
        Assert.Equal(2, (await new ChannelIntegrationRepository(NewContext()).ListForClientAsync(c.Id)).Count);
    }
}

public class WhatsAppRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static WhatsAppTemplate Template(Guid clientId, string name, string language = "en_US", int daysAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Name = name, Language = language, Body = "hello", CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Templates_are_found_by_id_name_and_meta_id()
    {
        var c = MakeClient(); var other = MakeClient();
        var t1 = Template(c.Id, "welcome", daysAgo: 2); t1.MetaTemplateId = "meta-1";
        var t2 = Template(c.Id, "welcome", "ar", daysAgo: 1);
        await SeedAsync(c, other, t1, t2);

        await using var db = NewContext();
        var repo = new WhatsAppTemplateRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, t1.Id));
        Assert.Null(await repo.GetAsync(other.Id, t1.Id));
        Assert.Equal([t2.Id, t1.Id], (await repo.ListForClientReadOnlyAsync(c.Id)).Select(t => t.Id));
        Assert.Equal(t1.Id, (await repo.FindByProviderTemplateIdAsync(c.Id, "meta-1"))!.Id);
        Assert.Null(await repo.FindByProviderTemplateIdAsync(other.Id, "meta-1"));
        Assert.Equal(t2.Id, (await repo.FindByNameAsync(c.Id, "welcome", "ar"))!.Id);
        Assert.Null(await repo.FindByNameAsync(c.Id, "welcome", "fr"));

        var added = Template(c.Id, "new");
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.Equal(3, (await new WhatsAppTemplateRepository(NewContext()).ListForClientReadOnlyAsync(c.Id)).Count);
    }

    [PostgresFact]
    public async Task Health_events_are_listed_newest_first_and_deduplicated_by_key()
    {
        var c = MakeClient();
        var integration = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = c.Id, Channel = ChannelType.WhatsApp, CreatedAt = Now, UpdatedAt = Now };
        WhatsAppHealthEvent Event(string type, int minutesAgo) => new()
        {
            Id = Guid.NewGuid(), ClientId = c.Id, ChannelIntegrationId = integration.Id, EventType = type, OccurredAt = Now.AddMinutes(-minutesAgo), CreatedAt = Now,
        };
        var e1 = Event("FLAGGED", 10); var e2 = Event("UNFLAGGED", 5);
        await SeedAsync(c, integration, e1, e2);

        await using var db = NewContext();
        var repo = new WhatsAppHealthEventRepository(db);
        Assert.Equal([e2.Id, e1.Id], (await repo.ListForClientAsync(c.Id, 0, 10)).Select(e => e.Id));
        Assert.Equal([e1.Id], (await repo.ListForClientAsync(c.Id, 1, 10)).Select(e => e.Id));
        Assert.True(await repo.ExistsAsync(integration.Id, "FLAGGED", e1.OccurredAt));
        Assert.False(await repo.ExistsAsync(integration.Id, "FLAGGED", e2.OccurredAt));

        repo.Add(Event("DOWNGRADE", 1));
        await db.SaveChangesAsync();
        Assert.Equal(3, (await new WhatsAppHealthEventRepository(NewContext()).ListForClientAsync(c.Id, 0, 10)).Count);
    }
}

public class UserSettingsAndEventRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Users_can_be_read_by_id_and_deleted()
    {
        IdentityUser Make(string email) => new() { Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        var u1 = Make($"a-{Guid.NewGuid():N}@x.com"); var u2 = Make($"b-{Guid.NewGuid():N}@x.com"); var u3 = Make($"c-{Guid.NewGuid():N}@x.com");
        await SeedAsync(u1, u2, u3);

        await using var db = NewContext();
        var repo = new UserRepository(db);
        Assert.Equal([u1.Email, u2.Email], (await repo.ListEmailsAsync([u1.Id, u2.Id, "ghost"])).Order());
        await repo.DeleteAsync([u1.Id, u2.Id]);
        Assert.Empty(await repo.ListEmailsAsync([u1.Id, u2.Id]));
        Assert.Single(await repo.ListEmailsAsync([u3.Id]));
    }

    [PostgresFact]
    public async Task Knowledge_search_settings_round_trip_and_detach()
    {
        var c = MakeClient(); var other = MakeClient();
        await SeedAsync(c, other);
        var settings = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(), ClientId = c.Id, EmbeddingModel = "text-embedding-3-small", VectorDimension = 1536, ChunkSizeTokens = 500,
            ChunkOverlapTokens = 50, TopK = 5, MinimumSimilarity = 0.3, CreatedAt = Now, UpdatedAt = Now,
        };
        await using (var db = NewContext())
        {
            var repo = new KnowledgeSettingsRepository(db);
            repo.Add(settings);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var r = new KnowledgeSettingsRepository(read);
        Assert.Equal(5, (await r.GetReadOnlyAsync(c.Id))!.TopK);
        Assert.Null(await r.GetReadOnlyAsync(other.Id));
        var tracked = (await r.GetAsync(c.Id))!;
        Assert.Equal(EntityState.Unchanged, read.Entry(tracked).State);
        r.Detach(tracked);
        Assert.Equal(EntityState.Detached, read.Entry(tracked).State);
    }

    [PostgresFact]
    public async Task TikTok_integration_per_client()
    {
        var c = MakeClient(); var other = MakeClient();
        await SeedAsync(c, other);
        await using (var db = NewContext())
        {
            new TikTokIntegrationRepository(db).Add(new TikTokIntegration { Id = Guid.NewGuid(), ClientId = c.Id, CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }
        await using var read = NewContext();
        var repo = new TikTokIntegrationRepository(read);
        Assert.NotNull(await repo.GetAsync(c.Id));
        Assert.Null(await repo.GetAsync(other.Id));
    }

    [PostgresFact]
    public async Task Config_settings_are_listed_in_order_and_can_be_added_and_removed()
    {
        var section = "Zz" + Guid.NewGuid().ToString("N")[..6];
        ConfigSetting Setting(string key) => new() { Section = section, Key = key, Value = "1", UpdatedAt = Now };
        await using (var db = NewContext())
        {
            var repo = new SettingRepository(db);
            repo.Add(Setting("B")); repo.Add(Setting("A"));
            await db.SaveChangesAsync();
        }

        await using var db2 = NewContext();
        var r = new SettingRepository(db2);
        Assert.Equal(["A", "B"], (await r.ListReadOnlyAsync()).Where(s => s.Section == section).Select(s => s.Key));
        var found = (await r.GetForUpdateAsync(section, "A"))!;
        Assert.Null(await r.GetForUpdateAsync(section, "missing"));
        r.Remove(found);
        await db2.SaveChangesAsync();
        Assert.Single((await new SettingRepository(NewContext()).ListReadOnlyAsync()).Where(s => s.Section == section));
    }

    [PostgresFact]
    public async Task Events_are_added()
    {
        var c = MakeClient();
        await SeedAsync(c);
        var entry = new EventLog { Id = Guid.NewGuid(), ClientId = c.Id, EventType = EventTypes.OrderConfirmed, CreatedAt = Now };
        await using (var db = NewContext())
        {
            new EventLogRepository(db).Add(entry);
            await db.SaveChangesAsync();
        }
        await using var check = NewContext();
        Assert.True(await check.Events.AnyAsync(e => e.Id == entry.Id));
    }
}

public class CalendarRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static CalendarIntegration Integration(Guid clientId, string provider, string status = CalendarIntegrationStatus.Connected) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Provider = provider, Status = status, CreatedAt = Now, UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Integrations_are_found_with_their_calendars_and_scoped_to_the_client()
    {
        var c = MakeClient(); var other = MakeClient();
        var google = Integration(c.Id, "google");
        var outlook = Integration(c.Id, "outlook");
        var cal = new CalendarIntegrationCalendar { Id = Guid.NewGuid(), CalendarIntegrationId = google.Id, ExternalCalendarId = "ext", Name = "Main", CreatedAt = Now };
        await SeedAsync(c, other, google, outlook, cal);

        await using var db = NewContext();
        var repo = new CalendarIntegrationRepository(db);
        Assert.Single((await repo.GetWithCalendarsAsync(c.Id, "google"))!.Calendars);
        Assert.Null(await repo.GetWithCalendarsAsync(other.Id, "google"));
        Assert.Equal(2, (await repo.ListWithCalendarsReadOnlyAsync(c.Id)).Count);
        Assert.NotNull(await repo.GetByIdAsync(c.Id, google.Id));
        Assert.Null(await repo.GetByIdAsync(other.Id, google.Id));
    }

    [PostgresFact]
    public async Task Sync_targets_need_connected_enabled_selected_and_a_token()
    {
        // Provider is unique per client and limited to google/outlook, so every variant gets its own client.
        async Task<bool> IsTargetAsync(Action<CalendarIntegration> tweak)
        {
            var c = MakeClient();
            var i = Integration(c.Id, "google");
            i.SyncEnabled = true; i.SelectedCalendarId = "cal"; i.AccessToken = "tok";
            tweak(i);
            await SeedAsync(c, i);
            await using var db = NewContext();
            return (await new CalendarIntegrationRepository(db).ListSyncTargetsAsync(c.Id)).Any(x => x.Id == i.Id);
        }

        Assert.True(await IsTargetAsync(_ => { }));
        Assert.False(await IsTargetAsync(i => i.SyncEnabled = false));
        Assert.False(await IsTargetAsync(i => i.SelectedCalendarId = null));
        Assert.False(await IsTargetAsync(i => i.AccessToken = null));
        Assert.False(await IsTargetAsync(i => i.Status = CalendarIntegrationStatus.Disconnected));
    }
    [PostgresFact]
    public async Task Calendars_can_be_replaced_and_removed_and_syncs_tracked_per_appointment()
    {
        var c = MakeClient();
        var lead = MakeLead(c.Id);
        var appt = MakeAppointment(c.Id, lead.Id, Now.AddDays(1));
        var integration = Integration(c.Id, "google");
        var old = new CalendarIntegrationCalendar { Id = Guid.NewGuid(), CalendarIntegrationId = integration.Id, ExternalCalendarId = "old", Name = "Old", CreatedAt = Now };
        await SeedAsync(c, lead, appt, integration, old);

        await using (var db = NewContext())
        {
            var repo = new CalendarIntegrationRepository(db);
            var loaded = (await repo.GetWithCalendarsAsync(c.Id, "google"))!;
            repo.ReplaceCalendars(loaded, [new CalendarIntegrationCalendar { Id = Guid.NewGuid(), CalendarIntegrationId = integration.Id, ExternalCalendarId = "new", Name = "New", CreatedAt = Now }]);
            repo.AddSync(new AppointmentCalendarSync { Id = Guid.NewGuid(), AppointmentId = appt.Id, CalendarIntegrationId = integration.Id, CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repo = new CalendarIntegrationRepository(db);
            var loaded = (await repo.GetWithCalendarsAsync(c.Id, "google"))!;
            Assert.Equal(["new"], loaded.Calendars.Select(x => x.ExternalCalendarId));
            Assert.NotNull(await repo.GetSyncAsync(appt.Id, integration.Id));
            Assert.Null(await repo.GetSyncAsync(Guid.NewGuid(), integration.Id));
            repo.RemoveCalendars(loaded);
            await db.SaveChangesAsync();
        }

        await using var check = NewContext();
        Assert.Empty((await new CalendarIntegrationRepository(check).GetWithCalendarsAsync(c.Id, "google"))!.Calendars);
    }
}

public class CampaignRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static Campaign Campaign(Guid clientId, string name = "Spring", int daysAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, Name = name, CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now,
    };

    private static CampaignRecipient Recipient(Campaign c, Lead lead, string status = CampaignRecipientStatus.Queued, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClientId = c.ClientId, CampaignId = c.Id, LeadId = lead.Id, PhoneNumber = "+1" + Guid.NewGuid().ToString("N")[..8],
        Status = status, CreatedAt = Now.AddMinutes(-minutesAgo), UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Campaigns_are_read_scoped_and_listed_newest_first()
    {
        var client = MakeClient(); var other = MakeClient();
        var template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = client.Id, Name = "tpl", Body = "b", CreatedAt = Now, UpdatedAt = Now };
        var old = Campaign(client.Id, "Old", 3); var recent = Campaign(client.Id, "Recent", 1); recent.WhatsAppTemplateId = template.Id;
        await SeedAsync(client, other, template, old, recent);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        Assert.NotNull(await repo.GetAsync(client.Id, old.Id));
        Assert.Null(await repo.GetAsync(other.Id, old.Id));
        Assert.NotNull(await repo.GetByIdAsync(old.Id));
        Assert.Equal("tpl", (await repo.GetWithTemplateAsync(client.Id, recent.Id))!.WhatsAppTemplate!.Name);
        Assert.Null(await repo.GetWithTemplateAsync(other.Id, recent.Id));
        Assert.Equal([recent.Id, old.Id], (await repo.ListWithTemplateAsync(client.Id)).Select(c => c.Id));

        var added = Campaign(client.Id, "Added");
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.Equal(3, (await new CampaignRepository(NewContext()).ListWithTemplateAsync(client.Id)).Count);
    }

    [PostgresFact]
    public async Task Recipients_are_listed_batched_and_counted()
    {
        var client = MakeClient();
        var camp = Campaign(client.Id); var otherCamp = Campaign(client.Id, "Other");
        var l1 = MakeLead(client.Id, "A", "1"); var l2 = MakeLead(client.Id, "B", "2"); var l3 = MakeLead(client.Id, "C", "3"); var l4 = MakeLead(client.Id, "D", "4");
        await SeedAsync(client, camp, otherCamp, l1, l2, l3, l4);
        var r1 = Recipient(camp, l1, CampaignRecipientStatus.Queued, 3);
        var r2 = Recipient(camp, l2, CampaignRecipientStatus.Queued, 2);
        var r3 = Recipient(camp, l3, CampaignRecipientStatus.Sent, 1);
        var r4 = Recipient(otherCamp, l4, CampaignRecipientStatus.Queued, 0);
        await SeedAsync(r1, r2, r3, r4);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        Assert.Equal(4, (await repo.ListRecipientsAsync([camp.Id, otherCamp.Id])).Count);
        var withLead = await repo.ListRecipientsWithLeadAsync(camp.Id);
        Assert.Equal([r1.Id, r2.Id, r3.Id], withLead.Select(r => r.Id));
        Assert.All(withLead, r => Assert.NotNull(r.Lead));
        Assert.Equal([r3.Id], (await repo.ListRecipientsInStatusAsync(camp.Id, [CampaignRecipientStatus.Sent])).Select(r => r.Id));
        Assert.Equal([r1.Id], (await repo.ListQueuedBatchAsync(camp.Id, 1)).Select(r => r.Id));
        Assert.Equal(2, await repo.CountRecipientsInStatusAsync(camp.Id, CampaignRecipientStatus.Queued));
        Assert.Equal(r2.Id, (await repo.GetRecipientAsync(r2.Id))!.Id);
        Assert.Null(await repo.GetRecipientAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task The_latest_replied_send_without_a_booking_is_found_within_the_window()
    {
        var client = MakeClient(); var lead = MakeLead(client.Id);
        var c1 = Campaign(client.Id, "A"); var c2 = Campaign(client.Id, "B"); var c3 = Campaign(client.Id, "C"); var c4 = Campaign(client.Id, "D");
        await SeedAsync(client, lead, c1, c2, c3, c4);
        var replied = DateTimeOffset.UtcNow;
        var old = Recipient(c1, lead, CampaignRecipientStatus.Replied); old.SentAt = replied.AddDays(-40); old.RepliedAt = replied.AddDays(-30);
        var recent = Recipient(c2, lead, CampaignRecipientStatus.Replied); recent.SentAt = replied.AddDays(-5); recent.RepliedAt = replied.AddDays(-3);
        var newest = Recipient(c3, lead, CampaignRecipientStatus.Replied); newest.SentAt = replied.AddDays(-2); newest.RepliedAt = replied.AddDays(-1);
        var alreadyBooked = Recipient(c4, lead, CampaignRecipientStatus.Booked); alreadyBooked.SentAt = replied; alreadyBooked.RepliedAt = replied; alreadyBooked.BookedAt = replied;
        await SeedAsync(old, recent, newest, alreadyBooked);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        Assert.Equal(newest.Id, (await repo.FindLatestRepliedAwaitingBookingAsync(client.Id, lead.Id, replied.AddDays(-14)))!.Id);
        Assert.Equal(old.Id, (await repo.FindLatestRepliedAwaitingBookingAsync(client.Id, lead.Id, replied.AddDays(-60)))!.Id == newest.Id ? old.Id : newest.Id);
        Assert.Null(await repo.FindLatestRepliedAwaitingBookingAsync(client.Id, lead.Id, replied.AddDays(1)));
        Assert.Null(await repo.FindLatestRepliedAwaitingBookingAsync(Guid.NewGuid(), lead.Id, replied.AddDays(-60)));
    }

    [PostgresFact]
    public async Task The_latest_unreplied_send_is_found_for_a_lead()
    {
        var client = MakeClient(); var other = MakeClient();
        var camp = Campaign(client.Id); var lead = MakeLead(client.Id);
        await SeedAsync(client, other, camp, lead);
        var leads = Enumerable.Range(0, 4).Select(_ => MakeLead(client.Id)).ToList(); await SeedAsync(leads.Cast<object>().ToArray());
        var oldSend = Recipient(camp, lead, CampaignRecipientStatus.Sent); oldSend.SentAt = Now.AddDays(-5);
        var camp2 = Campaign(client.Id, "C2"); var camp3 = Campaign(client.Id, "C3"); var camp4 = Campaign(client.Id, "C4"); await SeedAsync(camp2, camp3, camp4);
        var newSend = Recipient(camp2, lead, CampaignRecipientStatus.Sent); newSend.SentAt = Now.AddDays(-1);
        var replied = Recipient(camp3, lead, CampaignRecipientStatus.Sent); replied.SentAt = Now; replied.RepliedAt = Now;
        var unsent = Recipient(camp4, lead);
        await SeedAsync(oldSend, newSend, replied, unsent);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        var found = (await repo.FindLatestAwaitingReplyAsync(client.Id, lead.Id))!;
        Assert.Equal(newSend.Id, found.Id);
        Assert.NotNull(found.Campaign);
        Assert.Null(await repo.FindLatestAwaitingReplyAsync(other.Id, lead.Id));

        var added = Recipient(camp, MakeLead(client.Id));
        await using var write = NewContext();
        new CampaignRepository(write).AddRecipient(added);
        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());   // the lead was never saved: FK holds
    }

    [PostgresFact]
    public async Task Due_campaigns_are_scheduled_ones_whose_time_has_come_and_running_ones_of_active_clients()
    {
        var client = MakeClient(); var inactive = MakeClient(); inactive.IsActive = false;
        var now = DateTimeOffset.UtcNow;
        Campaign With(Guid clientId, string status, DateTimeOffset? scheduledAt = null)
        {
            var c = Campaign(clientId, status); c.Status = status; c.ScheduledAt = scheduledAt; return c;
        }
        var due = With(client.Id, CampaignStatus.Scheduled, now.AddMinutes(-5));
        var later = With(client.Id, CampaignStatus.Scheduled, now.AddMinutes(5));
        var running = With(client.Id, CampaignStatus.Running);
        var stopped = With(client.Id, CampaignStatus.Paused, now.AddMinutes(-5));
        var draft = With(client.Id, CampaignStatus.Draft, now.AddMinutes(-5));
        var finished = With(client.Id, CampaignStatus.Completed);
        var offClient = With(inactive.Id, CampaignStatus.Running);
        await SeedAsync(client, inactive, due, later, running, stopped, draft, finished, offClient);

        var found = await new CampaignRepository(NewContext()).ListDueForSendingReadOnlyAsync(now);

        var mine = found.Where(d => d.ClientId == client.Id || d.ClientId == inactive.Id).ToList();
        Assert.Equal([due.Id, running.Id], mine.Select(d => d.Id));                 // scheduled first, then running
        Assert.Equal([CampaignStatus.Scheduled, CampaignStatus.Running], mine.Select(d => d.Status));
    }

    [PostgresFact]
    public async Task Status_reads_are_fresh_and_status_changes_are_conditional()
    {
        var client = MakeClient(); var other = MakeClient();
        var c = Campaign(client.Id); c.Status = CampaignStatus.Running;
        await SeedAsync(client, other, c);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        var tracked = (await repo.GetAsync(client.Id, c.Id))!;
        Assert.Equal(CampaignStatus.Running, await repo.GetStatusReadOnlyAsync(c.Id));
        Assert.Null(await repo.GetStatusReadOnlyAsync(Guid.NewGuid()));

        // Another request stops it; this context still holds the old tracked copy but the fresh read sees the stop.
        var when = DateTimeOffset.UtcNow;
        Assert.False(await new CampaignRepository(NewContext()).TryChangeStatusAsync(other.Id, c.Id, [CampaignStatus.Running], CampaignStatus.Paused, when));
        Assert.True(await new CampaignRepository(NewContext()).TryChangeStatusAsync(client.Id, c.Id, [CampaignStatus.Scheduled, CampaignStatus.Running], CampaignStatus.Paused, when));
        Assert.Equal(CampaignStatus.Running, tracked.Status);
        Assert.Equal(CampaignStatus.Paused, await repo.GetStatusReadOnlyAsync(c.Id));

        // Not in a "from" status any more: nothing changes.
        Assert.False(await new CampaignRepository(NewContext()).TryChangeStatusAsync(client.Id, c.Id, [CampaignStatus.Running], CampaignStatus.Completed, when));
        Assert.Equal(CampaignStatus.Paused, await repo.GetStatusReadOnlyAsync(c.Id));
    }

    [PostgresFact]
    public async Task Only_one_sender_at_a_time_holds_a_campaign()
    {
        var campaignId = Guid.NewGuid();
        await using var first = NewContext();
        await using var second = NewContext();

        var held = await new CampaignRepository(first).TryLockForSendingAsync(campaignId);
        Assert.NotNull(held);
        Assert.Null(await new CampaignRepository(second).TryLockForSendingAsync(campaignId));
        // A different campaign is independent.
        var otherLock = await new CampaignRepository(second).TryLockForSendingAsync(Guid.NewGuid());
        Assert.NotNull(otherLock);
        await otherLock!.DisposeAsync();

        await held!.DisposeAsync();
        await held.DisposeAsync(); // releasing twice is harmless
        var again = await new CampaignRepository(second).TryLockForSendingAsync(campaignId);
        Assert.NotNull(again);
        await again!.DisposeAsync();
    }
}
