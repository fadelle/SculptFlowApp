using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Repositories.Calendars;
using PlasticSurgery.Persistence.Repositories.Campaigns;
using PlasticSurgery.Persistence.Repositories.Channels;
using PlasticSurgery.Persistence.Repositories.Configuration;
using PlasticSurgery.Persistence.Repositories.Events;
using PlasticSurgery.Persistence.Repositories.Knowledge;
using PlasticSurgery.Persistence.Repositories.Notifications;
using PlasticSurgery.Persistence.Repositories.Procedures;
using PlasticSurgery.Persistence.Repositories.TikTok;
using PlasticSurgery.Persistence.Repositories.Users;
using PlasticSurgery.Persistence.Repositories.WhatsApp;

namespace PlasticSurgery.Tests.Repositories;

public class ProcedureRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Procedures_are_listed_by_name_and_filtered_to_active()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var b = MakeProcedure(c.Id, "Botox"); var a = MakeProcedure(c.Id, "Abdominoplasty"); var off = MakeProcedure(c.Id, "Chin", active: false);
        var foreign = MakeProcedure(other.Id, "Foreign");
        await SeedAsync(c, other, b, a, off, foreign);

        await using var db = NewContext();
        var repo = new ProcedureRepository(db);
        Assert.Equal(["Abdominoplasty", "Botox", "Chin"], (await repo.ListAsync(c.Id, false)).Select(p => p.Name));
        Assert.Equal(["Abdominoplasty", "Botox"], (await repo.ListAsync(c.Id, true)).Select(p => p.Name));
    }

    [PostgresFact]
    public async Task Lookups_activity_and_name_clashes()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var p = MakeProcedure(c.Id, "Rhinoplasty"); var off = MakeProcedure(c.Id, "Retired", active: false);
        await SeedAsync(c, other, p, off);

        await using var db = NewContext();
        var repo = new ProcedureRepository(db);
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

        var added = MakeProcedure(c.Id, "Facelift");
        repo.Add(added);
        await db.SaveChangesAsync();
        await using var check = NewContext();
        Assert.True(await new ProcedureRepository(check).NameExistsAsync(c.Id, "facelift", null));
    }
}

public class ProcedureBookingRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Bookings_are_scoped_filtered_ordered_and_loadable()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var proc = MakeProcedure(c.Id); var lead = MakeLead(c.Id);
        ProcedureBooking Booking(string status, int daysAgo) => new()
        {
            Id = Guid.NewGuid(), ClinicId = c.Id, LeadId = lead.Id, ProcedureId = proc.Id, Status = status,
            CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now.AddDays(-daysAgo),
        };
        var older = Booking(ProcedureBookingStatus.Considering, 2);
        var newer = Booking(ProcedureBookingStatus.Booked, 1);
        await SeedAsync(c, other, proc, lead, older, newer);

        await using var db = NewContext();
        var repo = new ProcedureBookingRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, older.Id));
        Assert.Null(await repo.GetAsync(other.Id, older.Id));

        var (items, total) = await repo.ListAsync(c.Id, null, 0, 10);
        Assert.Equal(2, total);
        Assert.Equal([newer.Id, older.Id], items.Select(b => b.Id));
        Assert.NotNull(items[0].Lead);
        Assert.NotNull(items[0].Procedure);
        Assert.Equal([newer.Id], (await repo.ListAsync(c.Id, ProcedureBookingStatus.Booked, 0, 10)).Items.Select(b => b.Id));
        Assert.Equal([older.Id], (await repo.ListAsync(c.Id, null, 1, 10)).Items.Select(b => b.Id));

        var fresh = new ProcedureBooking { Id = Guid.NewGuid(), ClinicId = c.Id, LeadId = lead.Id, ProcedureId = proc.Id, CreatedAt = Now, UpdatedAt = Now };
        repo.Add(fresh);
        await db.SaveChangesAsync();
        await repo.LoadDetailsAsync(fresh);
        Assert.NotNull(fresh.Lead);
        Assert.NotNull(fresh.Procedure);
    }
}

public class NotificationRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static Notification Note(Guid clinicId, bool read, int minutesAgo) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Type = NotificationType.AppointmentBooked, Title = "t" + minutesAgo, IsRead = read,
        CreatedAt = Now.AddMinutes(-minutesAgo),
    };

    [PostgresFact]
    public async Task List_counts_unread_and_pages_newest_first()
    {
        var c = MakeClinic(); var other = MakeClinic();
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
    public async Task Mark_all_read_only_touches_this_clinics_unread_rows()
    {
        var c = MakeClinic(); var other = MakeClinic();
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
    private static ChannelIntegration Integration(Guid clinicId, string channel, string status = ChannelIntegrationStatus.Connected) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Channel = channel, Status = status, CreatedAt = Now, UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Lookups_by_clinic_channel_and_provider_identifiers()
    {
        var c = MakeClinic(); var other = MakeClinic();
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
        Assert.Equal(2, (await repo.ListForClinicAsync(c.Id)).Count);
        Assert.Equal([ChannelType.Telegram, ChannelType.WhatsApp], (await repo.ListForClinicReadOnlyAsync(c.Id)).Select(i => i.Channel).Order());
    }

    [PostgresFact]
    public async Task Connected_elsewhere_checks_and_connection_counts()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var bot = "bot-" + Guid.NewGuid().ToString("N")[..8]; var sender = "S-" + Guid.NewGuid().ToString("N")[..8];
        var tg = Integration(other.Id, ChannelType.Telegram); tg.TelegramBotId = bot;
        var wa = Integration(other.Id, ChannelType.WhatsApp); wa.Provider = ChannelProvider.Infobip; wa.ProviderSenderId = sender;
        var mine = Integration(c.Id, ChannelType.Telegram);
        var down = Integration(c.Id, ChannelType.WhatsApp, ChannelIntegrationStatus.Disconnected);
        await SeedAsync(c, other, tg, wa, mine, down);

        await using var db = NewContext();
        var repo = new ChannelIntegrationRepository(db);
        Assert.True(await repo.IsTelegramBotConnectedElsewhereAsync(bot, c.Id));
        Assert.False(await repo.IsTelegramBotConnectedElsewhereAsync(bot, other.Id));
        Assert.True(await repo.IsInfobipSenderConnectedElsewhereAsync(sender, c.Id));
        Assert.False(await repo.IsInfobipSenderConnectedElsewhereAsync(sender, other.Id));
        Assert.Equal(1, await repo.CountConnectedExceptAsync(c.Id, ChannelType.WhatsApp));
        Assert.Equal(0, await repo.CountConnectedExceptAsync(c.Id, ChannelType.Telegram));
    }

    [PostgresFact]
    public async Task Touching_the_webhook_time_and_adding()
    {
        var c = MakeClinic();
        var wa = Integration(c.Id, ChannelType.WhatsApp);
        await SeedAsync(c, wa);
        await using var db = NewContext();
        var repo = new ChannelIntegrationRepository(db);
        await repo.TouchLastWebhookAsync(wa.Id, Now);
        Assert.Equal(Now, (await repo.GetByIdReadOnlyAsync(wa.Id))!.LastWebhookAt);

        repo.Add(Integration(c.Id, ChannelType.Telegram));
        await db.SaveChangesAsync();
        Assert.Equal(2, (await new ChannelIntegrationRepository(NewContext()).ListForClinicAsync(c.Id)).Count);
    }
}

public class WhatsAppRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static WhatsAppTemplate Template(Guid clinicId, string name, string language = "en_US", int daysAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Name = name, Language = language, Body = "hello", CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Templates_are_found_by_id_name_and_provider_id()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var t1 = Template(c.Id, "welcome", daysAgo: 2); t1.MetaTemplateId = "meta-1";
        var t2 = Template(c.Id, "welcome", "ar", daysAgo: 1);
        var infobip = Template(other.Id, "promo"); infobip.MetaTemplateId = "ib-" + Guid.NewGuid().ToString("N")[..8]; infobip.Provider = ChannelProvider.Infobip;
        await SeedAsync(c, other, t1, t2, infobip);

        await using var db = NewContext();
        var repo = new WhatsAppTemplateRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, t1.Id));
        Assert.Null(await repo.GetAsync(other.Id, t1.Id));
        Assert.Equal([t2.Id, t1.Id], (await repo.ListForClinicAsync(c.Id)).Select(t => t.Id));
        Assert.Equal(t1.Id, (await repo.FindByProviderTemplateIdAsync(c.Id, "meta-1"))!.Id);
        Assert.Null(await repo.FindByProviderTemplateIdAsync(other.Id, "meta-1"));
        Assert.Equal(t2.Id, (await repo.FindByNameAsync(c.Id, "welcome", "ar"))!.Id);
        Assert.Null(await repo.FindByNameAsync(c.Id, "welcome", "fr"));
        Assert.Equal(other.Id, await repo.FindInfobipTemplateClinicIdAsync(infobip.MetaTemplateId));
        Assert.Null(await repo.FindInfobipTemplateClinicIdAsync("meta-1"));   // a Meta-owned id is not an Infobip template

        var added = Template(c.Id, "new");
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.Equal(3, (await new WhatsAppTemplateRepository(NewContext()).ListForClinicAsync(c.Id)).Count);
    }

    [PostgresFact]
    public async Task Health_events_are_listed_newest_first_and_deduplicated_by_key()
    {
        var c = MakeClinic();
        var integration = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = c.Id, Channel = ChannelType.WhatsApp, CreatedAt = Now, UpdatedAt = Now };
        WhatsAppHealthEvent Event(string type, int minutesAgo) => new()
        {
            Id = Guid.NewGuid(), ClinicId = c.Id, ChannelIntegrationId = integration.Id, EventType = type, OccurredAt = Now.AddMinutes(-minutesAgo), CreatedAt = Now,
        };
        var e1 = Event("FLAGGED", 10); var e2 = Event("UNFLAGGED", 5);
        await SeedAsync(c, integration, e1, e2);

        await using var db = NewContext();
        var repo = new WhatsAppHealthEventRepository(db);
        Assert.Equal([e2.Id, e1.Id], (await repo.ListForClinicAsync(c.Id, 0, 10)).Select(e => e.Id));
        Assert.Equal([e1.Id], (await repo.ListForClinicAsync(c.Id, 1, 10)).Select(e => e.Id));
        Assert.True(await repo.ExistsAsync(integration.Id, "FLAGGED", e1.OccurredAt));
        Assert.False(await repo.ExistsAsync(integration.Id, "FLAGGED", e2.OccurredAt));

        repo.Add(Event("DOWNGRADE", 1));
        await db.SaveChangesAsync();
        Assert.Equal(3, (await new WhatsAppHealthEventRepository(NewContext()).ListForClinicAsync(c.Id, 0, 10)).Count);
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
        var c = MakeClinic(); var other = MakeClinic();
        await SeedAsync(c, other);
        var settings = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(), ClinicId = c.Id, EmbeddingModel = "text-embedding-3-small", VectorDimension = 1536, ChunkSizeTokens = 500,
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
    public async Task TikTok_integration_per_clinic()
    {
        var c = MakeClinic(); var other = MakeClinic();
        await SeedAsync(c, other);
        await using (var db = NewContext())
        {
            new TikTokIntegrationRepository(db).Add(new TikTokIntegration { Id = Guid.NewGuid(), ClinicId = c.Id, CreatedAt = Now, UpdatedAt = Now });
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
        var c = MakeClinic();
        await SeedAsync(c);
        var entry = new EventLog { Id = Guid.NewGuid(), ClinicId = c.Id, EventType = EventTypes.ProcedureBooked, CreatedAt = Now };
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
    private static CalendarIntegration Integration(Guid clinicId, string provider, string status = CalendarIntegrationStatus.Connected) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Provider = provider, Status = status, CreatedAt = Now, UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Integrations_are_found_with_their_calendars_and_scoped_to_the_clinic()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var google = Integration(c.Id, "google");
        var outlook = Integration(c.Id, "outlook");
        var cal = new CalendarIntegrationCalendar { Id = Guid.NewGuid(), CalendarIntegrationId = google.Id, ExternalCalendarId = "ext", Name = "Main", CreatedAt = Now };
        await SeedAsync(c, other, google, outlook, cal);

        await using var db = NewContext();
        var repo = new CalendarIntegrationRepository(db);
        Assert.Single((await repo.GetWithCalendarsAsync(c.Id, "google"))!.Calendars);
        Assert.Null(await repo.GetWithCalendarsAsync(other.Id, "google"));
        Assert.Equal(2, (await repo.ListWithCalendarsAsync(c.Id)).Count);
        Assert.NotNull(await repo.GetByIdAsync(c.Id, google.Id));
        Assert.Null(await repo.GetByIdAsync(other.Id, google.Id));
    }

    [PostgresFact]
    public async Task Sync_targets_need_connected_enabled_selected_and_a_token()
    {
        // Provider is unique per clinic and limited to google/outlook, so every variant gets its own clinic.
        async Task<bool> IsTargetAsync(Action<CalendarIntegration> tweak)
        {
            var c = MakeClinic();
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
        var c = MakeClinic();
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
    private static Campaign Campaign(Guid clinicId, string name = "Spring", int daysAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Name = name, CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now,
    };

    private static CampaignRecipient Recipient(Campaign c, Lead lead, string status = CampaignRecipientStatus.Queued, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClinicId = c.ClinicId, CampaignId = c.Id, LeadId = lead.Id, PhoneNumber = "+1" + Guid.NewGuid().ToString("N")[..8],
        Status = status, CreatedAt = Now.AddMinutes(-minutesAgo), UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Campaigns_are_read_scoped_and_listed_newest_first()
    {
        var clinic = MakeClinic(); var other = MakeClinic();
        var template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClinicId = clinic.Id, Name = "tpl", Body = "b", CreatedAt = Now, UpdatedAt = Now };
        var old = Campaign(clinic.Id, "Old", 3); var recent = Campaign(clinic.Id, "Recent", 1); recent.WhatsAppTemplateId = template.Id;
        await SeedAsync(clinic, other, template, old, recent);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        Assert.NotNull(await repo.GetAsync(clinic.Id, old.Id));
        Assert.Null(await repo.GetAsync(other.Id, old.Id));
        Assert.NotNull(await repo.GetByIdAsync(old.Id));
        Assert.Equal("tpl", (await repo.GetWithTemplateAsync(clinic.Id, recent.Id))!.WhatsAppTemplate!.Name);
        Assert.Null(await repo.GetWithTemplateAsync(other.Id, recent.Id));
        Assert.Equal([recent.Id, old.Id], (await repo.ListWithTemplateAsync(clinic.Id)).Select(c => c.Id));

        var added = Campaign(clinic.Id, "Added");
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.Equal(3, (await new CampaignRepository(NewContext()).ListWithTemplateAsync(clinic.Id)).Count);
    }

    [PostgresFact]
    public async Task Recipients_are_listed_batched_and_counted()
    {
        var clinic = MakeClinic();
        var camp = Campaign(clinic.Id); var otherCamp = Campaign(clinic.Id, "Other");
        var l1 = MakeLead(clinic.Id, "A", "1"); var l2 = MakeLead(clinic.Id, "B", "2"); var l3 = MakeLead(clinic.Id, "C", "3"); var l4 = MakeLead(clinic.Id, "D", "4");
        await SeedAsync(clinic, camp, otherCamp, l1, l2, l3, l4);
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
    public async Task The_latest_unreplied_send_is_found_for_a_lead()
    {
        var clinic = MakeClinic(); var other = MakeClinic();
        var camp = Campaign(clinic.Id); var lead = MakeLead(clinic.Id);
        await SeedAsync(clinic, other, camp, lead);
        var leads = Enumerable.Range(0, 4).Select(_ => MakeLead(clinic.Id)).ToList(); await SeedAsync(leads.Cast<object>().ToArray());
        var oldSend = Recipient(camp, lead, CampaignRecipientStatus.Sent); oldSend.SentAt = Now.AddDays(-5);
        var camp2 = Campaign(clinic.Id, "C2"); var camp3 = Campaign(clinic.Id, "C3"); var camp4 = Campaign(clinic.Id, "C4"); await SeedAsync(camp2, camp3, camp4);
        var newSend = Recipient(camp2, lead, CampaignRecipientStatus.Sent); newSend.SentAt = Now.AddDays(-1);
        var replied = Recipient(camp3, lead, CampaignRecipientStatus.Sent); replied.SentAt = Now; replied.RepliedAt = Now;
        var unsent = Recipient(camp4, lead);
        await SeedAsync(oldSend, newSend, replied, unsent);

        await using var db = NewContext();
        var repo = new CampaignRepository(db);
        var found = (await repo.FindLatestAwaitingReplyAsync(clinic.Id, lead.Id))!;
        Assert.Equal(newSend.Id, found.Id);
        Assert.NotNull(found.Campaign);
        Assert.Null(await repo.FindLatestAwaitingReplyAsync(other.Id, lead.Id));

        var added = Recipient(camp, MakeLead(clinic.Id));
        await using var write = NewContext();
        new CampaignRepository(write).AddRecipient(added);
        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());   // the lead was never saved: FK holds
    }
}
