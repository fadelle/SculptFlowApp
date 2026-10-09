using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Repositories.Appointments;
using PlasticSurgery.Persistence.Repositories.Clinics;
using PlasticSurgery.Persistence.Repositories.Inbox;
using PlasticSurgery.Persistence.Repositories.Leads;

namespace PlasticSurgery.Tests.Repositories;

public class LeadRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Lookups_are_scoped_to_the_clinic()
    {
        var a = MakeClinic(); var b = MakeClinic();
        var proc = MakeProcedure(a.Id);
        var lead = MakeLead(a.Id, "Ann Smith", "+1555", proc.Id);
        lead.ExternalLeadId = "ext-1";
        await SeedAsync(a, b, proc, lead);

        await using var db = NewContext();
        var repo = new LeadRepository(db);
        Assert.Equal(lead.Id, (await repo.GetAsync(a.Id, lead.Id))!.Id);
        Assert.Null(await repo.GetAsync(b.Id, lead.Id));
        Assert.NotNull((await repo.GetAsync(a.Id, lead.Id))!.Procedure);
        Assert.NotNull(await repo.GetByIdAsync(lead.Id));
        Assert.NotNull(await repo.FindByExternalIdAsync(a.Id, "ext-1"));
        Assert.Null(await repo.FindByExternalIdAsync(b.Id, "ext-1"));
        Assert.NotNull(await repo.FindByPhoneAsync(a.Id, "+1555"));
        Assert.Null(await repo.FindByPhoneAsync(b.Id, "+1555"));
        Assert.True(await repo.ExistsAsync(a.Id, lead.Id));
        Assert.False(await repo.ExistsAsync(b.Id, lead.Id));
        Assert.Equal("Ann Smith", await repo.GetFullNameAsync(lead.Id));
        Assert.Null(await repo.GetFullNameAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task List_filters_searches_orders_and_pages()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var proc = MakeProcedure(c.Id);
        var l1 = MakeLead(c.Id, "Alice Wong", "111", proc.Id, LeadStatus.New, Now.AddDays(-3));
        var l2 = MakeLead(c.Id, "bob alison", "222", null, LeadStatus.Contacted, Now.AddDays(-2));
        var l3 = MakeLead(c.Id, "Carol", "333", null, LeadStatus.New, Now.AddDays(-1));
        l3.Email = "ALI@example.com";
        var foreign = MakeLead(other.Id, "Alice Foreign");
        await SeedAsync(c, other, proc, l1, l2, l3, foreign);

        await using var db = NewContext();
        var repo = new LeadRepository(db);

        var (all, total) = await repo.ListAsync(c.Id, null, null, null, 0, 50);
        Assert.Equal(3, total);
        Assert.Equal([l3.Id, l2.Id, l1.Id], all.Select(l => l.Id));   // newest first

        Assert.Equal(2, (await repo.ListAsync(c.Id, LeadStatus.New, null, null, 0, 50)).TotalCount);
        Assert.Equal([l1.Id], (await repo.ListAsync(c.Id, null, proc.Id, null, 0, 50)).Items.Select(l => l.Id));
        var ali = await repo.ListAsync(c.Id, null, null, " ali ", 0, 50);   // name, e-mail, case-insensitive, trimmed
        Assert.Equal(3, ali.TotalCount);
        Assert.Single((await repo.ListAsync(c.Id, null, null, "222", 0, 50)).Items);
        Assert.Equal(0, (await repo.ListAsync(c.Id, null, null, "zzz", 0, 50)).TotalCount);

        var page = await repo.ListAsync(c.Id, null, null, null, 1, 1);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(l2.Id, Assert.Single(page.Items).Id);
    }

    [PostgresFact]
    public async Task Sources_ids_and_adding()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var l1 = MakeLead(c.Id); l1.Source = "web";
        var l2 = MakeLead(c.Id); l2.Source = "ads";
        var l3 = MakeLead(c.Id); l3.Source = "web";
        var l4 = MakeLead(c.Id); l4.Source = "";
        var l5 = MakeLead(c.Id);
        var foreign = MakeLead(other.Id); foreign.Source = "referral";
        await SeedAsync(c, other, l1, l2, l3, l4, l5, foreign);

        await using var db = NewContext();
        var repo = new LeadRepository(db);
        Assert.Equal(["ads", "web"], await repo.ListDistinctSourcesAsync(c.Id));
        Assert.Equal(2, (await repo.ListByIdsAsync(c.Id, [l1.Id, l2.Id, foreign.Id])).Count);

        var added = MakeLead(c.Id, "New");
        repo.Add(added);
        await db.SaveChangesAsync();
        await using var check = NewContext();
        Assert.True(await new LeadRepository(check).ExistsAsync(c.Id, added.Id));
    }

    [PostgresFact]
    public async Task LoadProcedure_fills_the_navigation()
    {
        var c = MakeClinic();
        var proc = MakeProcedure(c.Id, "Facelift");
        var lead = MakeLead(c.Id, procedureId: proc.Id);
        await SeedAsync(c, proc, lead);
        await using var db = NewContext();
        var repo = new LeadRepository(db);
        var tracked = (await repo.GetByIdAsync(lead.Id))!;
        Assert.Null(tracked.Procedure);
        await repo.LoadProcedureAsync(tracked);
        Assert.Equal("Facelift", tracked.Procedure!.Name);
    }
}

public class ClinicRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private async Task<IdentityUser> SeedUserAsync(string email, string? fullName = null)
    {
        var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        await using var db = NewContext();
        db.Users.Add(user);
        if (fullName is not null) db.UserClaims.Add(new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "full_name", ClaimValue = fullName });
        await db.SaveChangesAsync();
        return user;
    }

    [PostgresFact]
    public async Task Clinics_can_be_read_added_and_deleted()
    {
        var c = MakeClinic("Alpha " + Guid.NewGuid().ToString("N")[..6]);
        await using (var db = NewContext())
        {
            var repo = new ClinicRepository(db);
            repo.Add(c);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var r = new ClinicRepository(read);
        Assert.Equal(c.Name, (await r.GetAsync(c.Id))!.Name);
        Assert.Equal(c.Name, (await r.GetReadOnlyAsync(c.Id))!.Name);
        Assert.True(await r.SlugExistsAsync(c.Slug));
        Assert.False(await r.SlugExistsAsync("no-such-slug"));
        Assert.Contains(await r.ListNamesAsync(), x => x.Id == c.Id);
        Assert.Contains(await r.ListByNamePrefixAsync("Alpha "), x => x.Id == c.Id);
        Assert.DoesNotContain(await r.ListByNamePrefixAsync("Zeta-nothing"), x => x.Id == c.Id);

        await r.DeleteAsync(c.Id);
        await using var after = NewContext();
        Assert.Null(await new ClinicRepository(after).GetAsync(c.Id));
    }

    [PostgresFact]
    public async Task Membership_lookups_only_see_active_memberships()
    {
        var active = MakeClinic(); var inactive = MakeClinic();
        await SeedAsync(active, inactive);
        var u1 = await SeedUserAsync($"u1-{Guid.NewGuid():N}@x.com", "Una One");
        var u2 = await SeedUserAsync($"u2-{Guid.NewGuid():N}@x.com");
        var u3 = await SeedUserAsync($"u3-{Guid.NewGuid():N}@x.com");

        await using (var db = NewContext())
        {
            var repo = new ClinicRepository(db);
            repo.AddMembership(new ClinicUser { Id = Guid.NewGuid(), ClinicId = active.Id, UserId = u1.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now });
            repo.AddMembership(new ClinicUser { Id = Guid.NewGuid(), ClinicId = inactive.Id, UserId = u2.Id, IsActive = false, CreatedAt = Now, UpdatedAt = Now });
            repo.AddMembership(new ClinicUser { Id = Guid.NewGuid(), ClinicId = active.Id, UserId = u3.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var r = new ClinicRepository(read);
        Assert.Equal(active.Id, (await r.GetClinicOfActiveMemberAsync(u1.Id))!.Id);
        Assert.Null(await r.GetClinicOfActiveMemberAsync(u2.Id));
        Assert.Equal(active.Id, await r.GetClinicIdOfActiveMemberAsync(u1.Id));
        Assert.Null(await r.GetClinicIdOfActiveMemberAsync(u2.Id));
        Assert.Null(await r.GetClinicIdOfActiveMemberAsync("nobody"));

        var staff = await r.ListStaffAsync(active.Id, "full_name");
        Assert.Equal(2, staff.Count);
        Assert.Equal("Una One", staff.Single(s => s.UserId == u1.Id).FullName);
        Assert.Null(staff.Single(s => s.UserId == u3.Id).FullName);
        Assert.Equal([u2.Id], await r.ListMemberUserIdsAsync(inactive.Id));
    }
}

public class ConversationAndMessageRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Conversation_lookups_are_scoped_and_typed()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var lead = MakeLead(c.Id);
        var convo = MakeConversation(c.Id, lead.Id, "whatsapp", "thread-1");
        convo.Mode = ConversationMode.Human;
        await SeedAsync(c, other, lead, convo);

        await using var db = NewContext();
        var repo = new ConversationRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, convo.Id));
        Assert.Null(await repo.GetAsync(other.Id, convo.Id));
        Assert.NotNull((await repo.GetWithLeadAsync(c.Id, convo.Id))!.Lead);
        Assert.NotNull(await repo.FindForLeadAsync(c.Id, lead.Id, "whatsapp"));
        Assert.Null(await repo.FindForLeadAsync(c.Id, lead.Id, "telegram"));
        Assert.NotNull(await repo.FindByExternalThreadAsync(c.Id, "whatsapp", "thread-1"));
        Assert.Null(await repo.FindByExternalThreadAsync(other.Id, "whatsapp", "thread-1"));
        Assert.True(await repo.ExistsAsync(c.Id, convo.Id));
        Assert.False(await repo.ExistsAsync(other.Id, convo.Id));
        Assert.True(await repo.IsInHumanModeAsync(c.Id, convo.Id));
        Assert.False(await repo.IsInHumanModeAsync(other.Id, convo.Id));
        Assert.False(await repo.IsInHumanModeAsync(c.Id, Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Inbox_lists_newest_first_with_last_message_and_unread_count()
    {
        var c = MakeClinic();
        var proc = MakeProcedure(c.Id, "Rhinoplasty");
        var leadA = MakeLead(c.Id, "Ann", "1", proc.Id); var leadB = MakeLead(c.Id, "Bob", "2");
        var old = MakeConversation(c.Id, leadA.Id, lastMessageAt: Now.AddHours(-5));
        var recent = MakeConversation(c.Id, leadB.Id, "telegram", lastMessageAt: Now.AddHours(-1));
        recent.LastReadAt = Now.AddHours(-3);
        var m1 = MakeMessage(old, content: "first", createdAt: Now.AddHours(-6));
        var m2 = MakeMessage(old, MessageDirection.Outbound, MessageOrigin.Ai, "reply", Now.AddHours(-5));
        var m3 = MakeMessage(old, MessageDirection.Outbound, MessageOrigin.System, "system note", Now.AddHours(-4));  // ignored as "last message"
        var n1 = MakeMessage(recent, content: "read", createdAt: Now.AddHours(-4));
        var n2 = MakeMessage(recent, content: "unread 1", createdAt: Now.AddHours(-2));
        var n3 = MakeMessage(recent, content: "unread 2", createdAt: Now.AddHours(-1));
        await SeedAsync(c, proc, leadA, leadB, old, recent, m1, m2, m3, n1, n2, n3);

        await using var db = NewContext();
        var repo = new ConversationRepository(db);
        var (items, total) = await repo.ListInboxAsync(c.Id, 0, 10);
        Assert.Equal(2, total);
        Assert.Equal([recent.Id, old.Id], items.Select(i => i.Id));
        Assert.Equal("unread 2", items[0].LastMessagePreview);
        Assert.Equal(2, items[0].UnreadCount);
        Assert.Equal("reply", items[1].LastMessagePreview);        // the system note is skipped
        Assert.Equal(1, items[1].UnreadCount);                      // never read: every inbound counts
        Assert.Equal("Rhinoplasty", items[1].ProcedureName);

        Assert.Equal(old.Id, Assert.Single((await repo.ListInboxAsync(c.Id, 1, 10)).Items).Id);
    }

    [PostgresFact]
    public async Task Mark_read_updates_only_the_clinics_conversation()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var lead = MakeLead(c.Id);
        var convo = MakeConversation(c.Id, lead.Id);
        await SeedAsync(c, other, lead, convo);

        await using var db = NewContext();
        var repo = new ConversationRepository(db);
        Assert.Equal(0, await repo.MarkReadAsync(other.Id, convo.Id, Now));
        Assert.Equal(1, await repo.MarkReadAsync(c.Id, convo.Id, Now));
        await using var check = NewContext();
        Assert.Equal(Now, (await check.Conversations.AsNoTracking().SingleAsync(x => x.Id == convo.Id)).LastReadAt);
    }

    [PostgresFact]
    public async Task Messages_are_listed_in_order_and_found_by_external_id()
    {
        var c = MakeClinic();
        var lead = MakeLead(c.Id);
        var convo = MakeConversation(c.Id, lead.Id);
        var first = MakeMessage(convo, content: "a", createdAt: Now.AddMinutes(-3), externalId: "ext-a");
        var second = MakeMessage(convo, content: "b", createdAt: Now.AddMinutes(-2));
        var third = MakeMessage(convo, content: "c", createdAt: Now.AddMinutes(-1));
        await SeedAsync(c, lead, convo, third, first, second);

        await using var db = NewContext();
        var repo = new MessageRepository(db);
        Assert.Equal(["a", "b", "c"], (await repo.ListForConversationAsync(convo.Id)).Select(m => m.Content));
        Assert.Equal(first.Id, (await repo.FindByExternalIdAsync(c.Id, "whatsapp", "ext-a"))!.Id);
        Assert.Null(await repo.FindByExternalIdAsync(c.Id, "telegram", "ext-a"));
        Assert.Equal("ext-a", await repo.GetExternalIdAsync(first.Id));
        Assert.Null(await repo.GetExternalIdAsync(second.Id));
        Assert.Equal(2, (await repo.ListByIdsReadOnlyAsync([first.Id, third.Id, Guid.NewGuid()])).Count);

        var added = MakeMessage(convo, content: "d");
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.Equal(4, (await new MessageRepository(NewContext()).ListForConversationAsync(convo.Id)).Count);
    }

    [PostgresFact]
    public async Task Inbound_customer_times_ignore_other_origins_and_other_clinics()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var lead = MakeLead(c.Id);
        var convo = MakeConversation(c.Id, lead.Id);
        var inbound = MakeMessage(convo, createdAt: Now);
        var outbound = MakeMessage(convo, MessageDirection.Outbound, MessageOrigin.Ai);
        var telegram = MakeMessage(convo, origin: MessageOrigin.TelegramCustomer);
        await SeedAsync(c, other, lead, convo, inbound, outbound, telegram);

        await using var db = NewContext();
        var repo = new MessageRepository(db);
        var times = await repo.ListInboundCustomerMessageTimesAsync(c.Id, [convo.Id]);
        Assert.Equal([(convo.Id, Now)], times);
        Assert.Empty(await repo.ListInboundCustomerMessageTimesAsync(other.Id, [convo.Id]));
    }
}

public class AppointmentRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private async Task<(Clinic Clinic, Lead Lead, Procedure Proc)> SeedBaseAsync(int? duration = 45)
    {
        var c = MakeClinic(); var proc = MakeProcedure(c.Id, duration: duration); var lead = MakeLead(c.Id, procedureId: proc.Id);
        await SeedAsync(c, proc, lead);
        return (c, lead, proc);
    }

    [PostgresFact]
    public async Task Gets_are_scoped_and_include_what_they_promise()
    {
        var (c, lead, proc) = await SeedBaseAsync();
        var other = MakeClinic();
        var appt = MakeAppointment(c.Id, lead.Id, Now.AddDays(1), procedureId: proc.Id);
        await SeedAsync(other, appt);

        await using var db = NewContext();
        var repo = new AppointmentRepository(db);
        Assert.NotNull(await repo.GetAsync(c.Id, appt.Id));
        Assert.Null(await repo.GetAsync(other.Id, appt.Id));
        var detailed = (await repo.GetWithDetailsAsync(c.Id, appt.Id))!;
        Assert.NotNull(detailed.Lead);
        Assert.NotNull(detailed.Procedure);
        Assert.NotNull(await repo.GetForLeadAsync(c.Id, lead.Id, appt.Id));
        Assert.Null(await repo.GetForLeadAsync(c.Id, Guid.NewGuid(), appt.Id));
        Assert.Equal("Rhinoplasty", (await repo.GetWithProcedureReadOnlyAsync(appt.Id))!.Procedure!.Name);
        Assert.Null(await repo.GetWithProcedureReadOnlyAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Upcoming_for_a_lead_means_future_and_booked_or_confirmed()
    {
        var (c, lead, _) = await SeedBaseAsync();
        var future2 = MakeAppointment(c.Id, lead.Id, Now.AddDays(2), AppointmentStatus.Confirmed);
        var future1 = MakeAppointment(c.Id, lead.Id, Now.AddDays(1));
        var canceled = MakeAppointment(c.Id, lead.Id, Now.AddDays(3), AppointmentStatus.Canceled);
        var past = MakeAppointment(c.Id, lead.Id, Now.AddDays(-1));
        await SeedAsync(future2, future1, canceled, past);

        await using var db = NewContext();
        var upcoming = await new AppointmentRepository(db).ListUpcomingForLeadAsync(c.Id, lead.Id, Now);
        Assert.Equal([future1.Id, future2.Id], upcoming.Select(a => a.Id));
    }

    [PostgresFact]
    public async Task List_filters_by_status_and_dates_and_pages()
    {
        var (c, lead, _) = await SeedBaseAsync();
        var a1 = MakeAppointment(c.Id, lead.Id, Now.AddDays(1));
        var a2 = MakeAppointment(c.Id, lead.Id, Now.AddDays(2), AppointmentStatus.Confirmed);
        var a3 = MakeAppointment(c.Id, lead.Id, Now.AddDays(3));
        await SeedAsync(a1, a2, a3);

        await using var db = NewContext();
        var repo = new AppointmentRepository(db);
        var (all, total) = await repo.ListAsync(c.Id, null, null, null, 0, 10);
        Assert.Equal(3, total);
        Assert.Equal([a1.Id, a2.Id, a3.Id], all.Select(a => a.Id));
        Assert.Equal(1, (await repo.ListAsync(c.Id, AppointmentStatus.Confirmed, null, null, 0, 10)).TotalCount);
        Assert.Equal([a2.Id], (await repo.ListAsync(c.Id, null, Now.AddDays(2), Now.AddDays(2), 0, 10)).Items.Select(a => a.Id));
        Assert.Equal([a2.Id], (await repo.ListAsync(c.Id, null, null, null, 1, 1)).Items.Select(a => a.Id));
    }

    [PostgresFact]
    public async Task Starting_between_is_half_open_and_ordered()
    {
        var (c, lead, _) = await SeedBaseAsync();
        var inside2 = MakeAppointment(c.Id, lead.Id, Now.AddHours(5));
        var inside1 = MakeAppointment(c.Id, lead.Id, Now);
        var atEnd = MakeAppointment(c.Id, lead.Id, Now.AddHours(10));
        await SeedAsync(inside2, inside1, atEnd);

        await using var db = NewContext();
        var found = await new AppointmentRepository(db).ListStartingBetweenAsync(c.Id, Now, Now.AddHours(10));
        Assert.Equal([inside1.Id, inside2.Id], found.Select(a => a.Id));
    }

    [PostgresFact]
    public async Task Needing_outcome_counts_past_booked_or_confirmed_only()
    {
        var (c, lead, _) = await SeedBaseAsync();
        await SeedAsync(
            MakeAppointment(c.Id, lead.Id, Now.AddDays(-1)),
            MakeAppointment(c.Id, lead.Id, Now.AddDays(-2), AppointmentStatus.Confirmed),
            MakeAppointment(c.Id, lead.Id, Now.AddDays(-3), AppointmentStatus.Attended),
            MakeAppointment(c.Id, lead.Id, Now.AddDays(1)));
        await using var db = NewContext();
        Assert.Equal(2, await new AppointmentRepository(db).CountNeedingOutcomeAsync(c.Id, Now));
    }

    [PostgresFact]
    public async Task Busy_rows_skip_canceled_rescheduled_and_the_excluded_appointment()
    {
        var (c, lead, _) = await SeedBaseAsync(duration: 45);
        var busy = MakeAppointment(c.Id, lead.Id, Now.AddHours(1), procedureId: (await NewContext().Procedures.FirstAsync(p => p.ClinicId == c.Id)).Id);
        var excluded = MakeAppointment(c.Id, lead.Id, Now.AddHours(2));
        var canceled = MakeAppointment(c.Id, lead.Id, Now.AddHours(3), AppointmentStatus.Canceled);
        var moved = MakeAppointment(c.Id, lead.Id, Now.AddHours(4), AppointmentStatus.Rescheduled);
        var outside = MakeAppointment(c.Id, lead.Id, Now.AddDays(5));
        await SeedAsync(busy, excluded, canceled, moved, outside);

        await using var db = NewContext();
        var repo = new AppointmentRepository(db);
        var rows = await repo.ListBusyAsync(c.Id, Now, Now.AddDays(1), excluded.Id);
        var row = Assert.Single(rows);
        Assert.Equal(busy.ScheduledStart, row.ScheduledStart);
        Assert.Equal(45, row.ProcedureMinutes);
        Assert.Equal(2, (await repo.ListBusyAsync(c.Id, Now, Now.AddDays(1), null)).Count);
    }

    [PostgresFact]
    public async Task Details_are_loaded_on_demand_and_the_booking_lock_can_be_taken()
    {
        var (c, lead, proc) = await SeedBaseAsync();
        var appt = MakeAppointment(c.Id, lead.Id, Now.AddDays(1), procedureId: proc.Id);
        var noProc = MakeAppointment(c.Id, lead.Id, Now.AddDays(2));
        await SeedAsync(appt, noProc);

        await using var db = NewContext();
        var repo = new AppointmentRepository(db);
        var tracked = (await repo.GetAsync(c.Id, appt.Id))!;
        await repo.LoadDetailsAsync(tracked);
        Assert.NotNull(tracked.Lead);
        Assert.NotNull(tracked.Procedure);
        var plain = (await repo.GetAsync(c.Id, noProc.Id))!;
        await repo.LoadDetailsAsync(plain);
        Assert.Null(plain.Procedure);

        await using var tx = await db.Database.BeginTransactionAsync();
        await repo.LockClinicForBookingAsync(c.Id);
        await tx.RollbackAsync();
    }

    [PostgresFact]
    public async Task Adding_saves_through_the_context()
    {
        var (c, lead, _) = await SeedBaseAsync();
        var appt = MakeAppointment(c.Id, lead.Id, Now.AddDays(1));
        await using (var db = NewContext())
        {
            new AppointmentRepository(db).Add(appt);
            await db.SaveChangesAsync();
        }
        await using var check = NewContext();
        Assert.NotNull(await new AppointmentRepository(check).GetAsync(c.Id, appt.Id));
    }
}

public class AvailabilityRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Rules_settings_and_exceptions_round_trip()
    {
        var c = MakeClinic(); var other = MakeClinic();
        await SeedAsync(c, other);
        var rule = new ClinicAvailabilityRule { Id = Guid.NewGuid(), ClinicId = c.Id, DayOfWeek = 1, IsOpen = true, StartTime = new(9, 0), EndTime = new(17, 0), CreatedAt = Now, UpdatedAt = Now };
        var settings = new ClinicBookingSettings { Id = Guid.NewGuid(), ClinicId = c.Id, BufferMinutes = 10, CreatedAt = Now, UpdatedAt = Now };
        var d1 = new DateOnly(2026, 7, 1); var d2 = new DateOnly(2026, 7, 10); var d3 = new DateOnly(2026, 8, 1);
        ClinicAvailabilityException Ex(DateOnly d, Guid clinic) => new() { Id = Guid.NewGuid(), ClinicId = clinic, Date = d, IsClosed = true, Reason = "holiday", CreatedAt = Now, UpdatedAt = Now };
        var e1 = Ex(d1, c.Id); var e2 = Ex(d2, c.Id); var e3 = Ex(d3, c.Id); var foreign = Ex(d1, other.Id);

        await using (var db = NewContext())
        {
            var repo = new AvailabilityRepository(db);
            repo.AddRule(rule);
            repo.AddBookingSettings(settings);
            repo.AddException(e1); repo.AddException(e2); repo.AddException(e3); repo.AddException(foreign);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var r = new AvailabilityRepository(read);
        Assert.Single(await r.ListRulesAsync(c.Id));
        Assert.Single(await r.ListRulesReadOnlyAsync(c.Id));
        Assert.Empty(await r.ListRulesAsync(other.Id));
        Assert.Equal(10, (await r.GetBookingSettingsAsync(c.Id))!.BufferMinutes);
        Assert.Equal(10, (await r.GetBookingSettingsReadOnlyAsync(c.Id))!.BufferMinutes);
        Assert.Null(await r.GetBookingSettingsAsync(other.Id));

        Assert.Equal(2, (await r.ListExceptionsAsync(c.Id, d1, d2)).Count);
        Assert.Equal([e1.Id, e2.Id, e3.Id], (await r.ListExceptionsFromAsync(c.Id, d1)).Select(e => e.Id));
        Assert.Equal([e3.Id], (await r.ListExceptionsFromAsync(c.Id, d2.AddDays(1))).Select(e => e.Id));
        Assert.Equal(e2.Id, (await r.GetExceptionByDateAsync(c.Id, d2))!.Id);
        Assert.Null(await r.GetExceptionByDateAsync(other.Id, d2));
        Assert.Equal(e1.Id, (await r.GetExceptionAsync(c.Id, e1.Id))!.Id);
        Assert.Null(await r.GetExceptionAsync(other.Id, e1.Id));

        var toRemove = (await r.GetExceptionAsync(c.Id, e1.Id))!;
        r.RemoveException(toRemove);
        await read.SaveChangesAsync();
        await using var after = NewContext();
        Assert.Null(await new AvailabilityRepository(after).GetExceptionAsync(c.Id, e1.Id));
    }
}
