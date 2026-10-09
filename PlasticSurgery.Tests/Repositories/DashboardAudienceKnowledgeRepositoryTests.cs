using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Persistence.Repositories.Campaigns;
using PlasticSurgery.Persistence.Repositories.Dashboard;
using PlasticSurgery.Persistence.Repositories.Knowledge;

namespace PlasticSurgery.Tests.Repositories;

public class DashboardRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static ProcedureBooking Booking(Guid clinicId, Guid leadId, Guid procedureId, string status, decimal? finalAmount = null,
        DateTimeOffset? created = null, DateTimeOffset? procedureDate = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, LeadId = leadId, ProcedureId = procedureId, Status = status, FinalAmount = finalAmount,
        ProcedureDate = procedureDate, CreatedAt = created ?? Now, UpdatedAt = created ?? Now,
    };

    [PostgresFact]
    public async Task Summary_counts_respect_the_clinic_and_the_date_range()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var proc = MakeProcedure(c.Id);
        var inRange = MakeLead(c.Id, createdAt: Now);
        var tooOld = MakeLead(c.Id, createdAt: Now.AddDays(-30));
        var foreign = MakeLead(other.Id, createdAt: Now);
        var attended = MakeAppointment(c.Id, inRange.Id, Now, AppointmentStatus.Attended);
        var booked = MakeAppointment(c.Id, tooOld.Id, Now.AddDays(5));
        var completed = Booking(c.Id, inRange.Id, proc.Id, ProcedureBookingStatus.Completed, 1500m, Now, Now);
        var completedOld = Booking(c.Id, tooOld.Id, proc.Id, ProcedureBookingStatus.Completed, 999m, Now.AddDays(-30), Now.AddDays(-30));
        var considering = Booking(c.Id, inRange.Id, proc.Id, ProcedureBookingStatus.Considering, null, Now);
        await SeedAsync(c, other, proc, inRange, tooOld, foreign);
        await SeedAsync(attended, booked, completed, completedOld, considering);

        await using var db = NewContext();
        var repo = new DashboardRepository(db);
        var ranged = await repo.GetSummaryCountsAsync(c.Id, Now.AddDays(-1), Now.AddDays(1));
        Assert.Equal(1, ranged.NewInterestedPeople);
        Assert.Equal(1, ranged.ConsultationsAttended);
        Assert.Equal(2, ranged.ConsultationsBooked);       // counted by when the appointment was created
        Assert.Equal(2, ranged.SurgeriesBooked);
        Assert.Equal(1500m, ranged.Revenue);

        var all = await repo.GetSummaryCountsAsync(c.Id, null, null);
        Assert.Equal(2, all.NewInterestedPeople);
        Assert.Equal(3, all.SurgeriesBooked);
        Assert.Equal(2499m, all.Revenue);
    }

    [PostgresFact]
    public async Task Lead_and_appointment_lists_filter_search_and_page()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var proc = MakeProcedure(c.Id, "Rhinoplasty");
        var l1 = MakeLead(c.Id, "Alice Wong", "111", proc.Id, LeadStatus.New, Now.AddDays(-2)); l1.Source = "Web";
        var l2 = MakeLead(c.Id, "Bob", "222", null, LeadStatus.Contacted, Now.AddDays(-1)); l2.Source = "ads";
        var foreign = MakeLead(other.Id, "Alice Foreign");
        var a1 = MakeAppointment(c.Id, l1.Id, Now.AddDays(1), procedureId: proc.Id);
        var a2 = MakeAppointment(c.Id, l2.Id, Now.AddDays(2), AppointmentStatus.Confirmed);
        await SeedAsync(c, other, proc, l1, l2, foreign);
        await SeedAsync(a1, a2);

        await using var db = NewContext();
        var repo = new DashboardRepository(db);
        var (leads, total) = await repo.ListLeadsAsync(c.Id, null, null, null, 0, 10);
        Assert.Equal(2, total);
        Assert.Equal([l2.Id, l1.Id], leads.Select(l => l.Id));        // newest first
        Assert.Equal("Rhinoplasty", leads[1].ProcedureName);
        Assert.Null(leads[0].ProcedureName);
        Assert.Equal([l2.Id], (await repo.ListLeadsAsync(c.Id, LeadStatus.Contacted, null, null, 0, 10)).Items.Select(l => l.Id));
        Assert.Equal([l1.Id], (await repo.ListLeadsAsync(c.Id, null, null, " WEB ", 0, 10)).Items.Select(l => l.Id));   // source: trimmed, case-insensitive
        Assert.Equal([l1.Id], (await repo.ListLeadsAsync(c.Id, null, "alice", null, 0, 10)).Items.Select(l => l.Id));
        Assert.Equal([l2.Id], (await repo.ListLeadsAsync(c.Id, null, "222", null, 0, 10)).Items.Select(l => l.Id));
        Assert.Equal([l1.Id], (await repo.ListLeadsAsync(c.Id, null, null, null, 1, 1)).Items.Select(l => l.Id));

        var (appts, apptTotal) = await repo.ListAppointmentsAsync(c.Id, null, null, 0, 10);
        Assert.Equal(2, apptTotal);
        Assert.Equal([a1.Id, a2.Id], appts.Select(a => a.Id));
        Assert.Equal([a2.Id], (await repo.ListAppointmentsAsync(c.Id, AppointmentStatus.Confirmed, null, 0, 10)).Items.Select(a => a.Id));
        Assert.Equal([a1.Id], (await repo.ListAppointmentsAsync(c.Id, null, "alice", 0, 10)).Items.Select(a => a.Id));
        Assert.Equal([a2.Id], (await repo.ListAppointmentsAsync(c.Id, null, null, 1, 1)).Items.Select(a => a.Id));
    }

    [PostgresFact]
    public async Task Procedure_stats_count_leads_bookings_and_completed_revenue_per_procedure()
    {
        var c = MakeClinic();
        var rhino = MakeProcedure(c.Id, "Rhinoplasty"); var botox = MakeProcedure(c.Id, "Botox");
        var l1 = MakeLead(c.Id, procedureId: rhino.Id); var l2 = MakeLead(c.Id, procedureId: rhino.Id); var l3 = MakeLead(c.Id, procedureId: botox.Id);
        var b1 = Booking(c.Id, l1.Id, rhino.Id, ProcedureBookingStatus.Completed, 2000m, Now, Now);
        var b2 = Booking(c.Id, l2.Id, rhino.Id, ProcedureBookingStatus.Booked, null, Now);
        var b3 = Booking(c.Id, l3.Id, botox.Id, ProcedureBookingStatus.Completed, 300m, Now, Now);
        await SeedAsync(c, rhino, botox, l1, l2, l3);
        await SeedAsync(b1, b2, b3);

        await using var db = NewContext();
        var stats = await new DashboardRepository(db).GetProcedureStatsAsync(c.Id, null, null);
        Assert.Equal(["Rhinoplasty", "Botox"], stats.Select(s => s.ProcedureName));      // by revenue
        var r = stats[0];
        Assert.Equal((2, 2, 1, 2000m), (r.LeadCount, r.BookingCount, r.CompletedCount, r.CompletedRevenue));
        Assert.Equal(300m, stats[1].CompletedRevenue);

        var none = await new DashboardRepository(NewContext()).GetProcedureStatsAsync(c.Id, Now.AddDays(10), Now.AddDays(20));
        Assert.All(none, s => Assert.Equal(0, s.LeadCount + s.BookingCount + s.CompletedCount));
    }

    [PostgresFact]
    public async Task Attention_counts_human_conversations_overdue_followups_and_missing_outcomes()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var open = MakeLead(c.Id);
        var overdue = MakeLead(c.Id); overdue.NextFollowupAt = Now.AddDays(-1);
        var overdueLost = MakeLead(c.Id, status: LeadStatus.Lost); overdueLost.NextFollowupAt = Now.AddDays(-1);
        var future = MakeLead(c.Id); future.NextFollowupAt = Now.AddDays(1);
        var human = MakeConversation(c.Id, open.Id); human.Mode = ConversationMode.Human;
        var approval = MakeConversation(c.Id, overdue.Id, "telegram"); approval.Mode = ConversationMode.Approval;
        var ai = MakeConversation(c.Id, future.Id);
        var closedHuman = MakeConversation(c.Id, overdueLost.Id); closedHuman.Mode = ConversationMode.Human; closedHuman.Status = ConversationStatus.Closed;
        var needsOutcome = MakeAppointment(c.Id, open.Id, Now.AddDays(-1));
        await SeedAsync(c, other, open, overdue, overdueLost, future);
        await SeedAsync(human, approval, ai, closedHuman, needsOutcome);

        await using var db = NewContext();
        var counts = await new DashboardRepository(db).GetAttentionCountsAsync(c.Id, Now);
        Assert.Equal((2, 1, 1), (counts.ConversationsNeedingStaff, counts.OverdueFollowups, counts.AppointmentsNeedingOutcome));
    }
}

public class CampaignAudienceRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private Lead Contactable(Guid clinicId, string name, Action<Lead>? tweak = null)
    {
        var l = MakeLead(clinicId, name, "+" + Guid.NewGuid().ToString("N")[..9]);
        tweak?.Invoke(l);
        return l;
    }

    private async Task<(int Count, List<Guid> Ids)> AudienceAsync(Guid clinicId, string type, CampaignAudienceFilters? filters = null, int days = 30)
    {
        await using var db = NewContext();
        var repo = new CampaignAudienceRepository(db);
        var f = filters ?? new CampaignAudienceFilters();
        var ids = (await repo.ListEligibleLeadsAsync(clinicId, type, f, days)).Select(l => l.Id).ToList();
        Assert.Equal(ids.Count, await repo.CountEligibleLeadsAsync(clinicId, type, f, days));
        return (ids.Count, ids);
    }

    [PostgresFact]
    public async Task Contactable_means_phone_opt_in_and_not_opted_out_in_this_clinic()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var ok = Contactable(c.Id, "ok");
        var noPhone = Contactable(c.Id, "nophone", l => l.Phone = null);
        var blankPhone = Contactable(c.Id, "blank", l => l.Phone = "");
        var optedOutFlag = Contactable(c.Id, "flag", l => l.MarketingOptIn = false);
        var optedOutAt = Contactable(c.Id, "at", l => l.OptedOutAt = Now);
        var foreign = Contactable(other.Id, "foreign");
        await SeedAsync(c, other, ok, noPhone, blankPhone, optedOutFlag, optedOutAt, foreign);

        var (_, ids) = await AudienceAsync(c.Id, CampaignAudienceType.AllEligible);
        Assert.Equal([ok.Id], ids);
        Assert.Empty((await AudienceAsync(c.Id, "nonsense")).Ids);   // unknown audience type matches nobody
    }

    [PostgresFact]
    public async Task Reactivation_wants_prior_interest_inactivity_and_no_booked_consultation()
    {
        var c = MakeClinic();
        var cold = Contactable(c.Id, "cold", l => { l.Status = LeadStatus.Contacted; l.LastContactAt = DateTimeOffset.UtcNow.AddDays(-90); });
        var untouchedNew = Contactable(c.Id, "new");                                                       // never interested
        var recent = Contactable(c.Id, "recent", l => { l.Status = LeadStatus.Contacted; l.LastContactAt = DateTimeOffset.UtcNow.AddDays(-2); });
        var booked = Contactable(c.Id, "booked", l => l.Status = LeadStatus.Contacted);
        var chatted = Contactable(c.Id, "chatted");                                                        // new, but a conversation exists
        var chattedRecently = Contactable(c.Id, "chat-recent", l => l.Status = LeadStatus.Contacted);
        await SeedAsync(c, cold, untouchedNew, recent, booked, chatted, chattedRecently);
        var oldConv = MakeConversation(c.Id, chatted.Id, lastMessageAt: DateTimeOffset.UtcNow.AddDays(-60));
        var freshConv = MakeConversation(c.Id, chattedRecently.Id, lastMessageAt: DateTimeOffset.UtcNow.AddDays(-1));
        var appt = MakeAppointment(c.Id, booked.Id, DateTimeOffset.UtcNow.AddDays(3));
        await SeedAsync(oldConv, freshConv, appt);

        var ids = (await AudienceAsync(c.Id, CampaignAudienceType.ReactivationNoConsultation)).Ids;
        Assert.Equivalent(new[] { cold.Id, chatted.Id }, ids);

        // A wider window pulls in the recently contacted lead; a custom inactive-days filter overrides the default.
        var wide = (await AudienceAsync(c.Id, CampaignAudienceType.ReactivationNoConsultation, new CampaignAudienceFilters(InactiveDays: 1))).Ids;
        Assert.Contains(recent.Id, wide);
        Assert.DoesNotContain(booked.Id, wide);
    }

    [PostgresFact]
    public async Task Custom_filters_combine()
    {
        var c = MakeClinic();
        var proc = MakeProcedure(c.Id);
        var target = Contactable(c.Id, "target", l =>
        {
            l.ProcedureId = proc.Id; l.Status = LeadStatus.Qualified; l.Source = "web"; l.QualificationStatus = LeadQualificationStatus.Hot;
            l.CountryCode = "LB"; l.City = "Beirut"; l.CreatedAt = Now.AddDays(-10); l.LastContactAt = Now.AddDays(-5);
        });
        var wrongCity = Contactable(c.Id, "wrong-city", l => { l.City = "Dubai"; l.CountryCode = "LB"; });
        var wrongStatus = Contactable(c.Id, "wrong-status", l => l.Status = LeadStatus.Contacted);
        var plain = Contactable(c.Id, "plain");
        await SeedAsync(c, proc, target, wrongCity, wrongStatus, plain);
        var attended = MakeAppointment(c.Id, target.Id, Now.AddDays(-2), AppointmentStatus.Attended);
        await SeedAsync(attended);

        async Task<List<Guid>> Run(CampaignAudienceFilters f) => (await AudienceAsync(c.Id, CampaignAudienceType.Custom, f)).Ids;

        Assert.Equal(4, (await Run(new CampaignAudienceFilters())).Count);
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(ProcedureId: proc.Id)));
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(LeadStatuses: [LeadStatus.Qualified])));
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(Sources: ["web"])));
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(QualificationStatuses: [LeadQualificationStatus.Hot])));
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(Cities: ["Beirut"])));
        Assert.Equal(2, (await Run(new CampaignAudienceFilters(Countries: ["LB"]))).Count);
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(AppointmentStatuses: [AppointmentStatus.Attended])));
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(CreatedAfter: Now.AddDays(-11), CreatedBefore: Now.AddDays(-9))));
        Assert.Equal([target.Id], await Run(new CampaignAudienceFilters(LastContactedAfter: Now.AddDays(-6), LastContactedBefore: Now.AddDays(-4))));
        Assert.Empty(await Run(new CampaignAudienceFilters(LastContactedAfter: Now.AddDays(-1))));
        // Inactive for 3 days: the lead contacted 5 days ago still qualifies; one who spoke yesterday would not.
        var inactive = await Run(new CampaignAudienceFilters(InactiveDays: 3));
        Assert.Contains(target.Id, inactive);
    }
}

public class KnowledgeRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static KnowledgeDocument Doc(Guid clinicId, string title, int minutesAgo = 0, bool active = true) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, Title = title, Content = "content", IsActive = active,
        CreatedAt = Now.AddMinutes(-minutesAgo), UpdatedAt = Now.AddMinutes(-minutesAgo),
    };

    private async Task<bool> HasVectorAsync()
    {
        await using var db = NewContext();
        return await db.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from pg_extension where extname = 'vector'").SingleAsync() > 0;
    }

    [PostgresFact]
    public async Task Documents_are_listed_newest_first_scoped_and_removable()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var older = Doc(c.Id, "older", 10); var newer = Doc(c.Id, "newer", 1); var foreign = Doc(other.Id, "foreign");
        await SeedAsync(c, other, older, newer, foreign);

        await using var db = NewContext();
        var repo = new KnowledgeDocumentRepository(db);
        Assert.Equal(["newer", "older"], (await repo.ListForClinicAsync(c.Id)).Select(d => d.Title));
        Assert.NotNull(await repo.GetAsync(c.Id, older.Id));
        Assert.Null(await repo.GetAsync(other.Id, older.Id));
        Assert.True(await repo.ExistsAsync(c.Id, older.Id));
        Assert.False(await repo.ExistsAsync(other.Id, older.Id));

        var added = Doc(c.Id, "added");
        repo.Add(added);
        repo.Remove((await repo.GetAsync(c.Id, older.Id))!);
        await db.SaveChangesAsync();
        Assert.Equal(["added", "newer"], (await new KnowledgeDocumentRepository(NewContext()).ListForClinicAsync(c.Id)).Select(d => d.Title).Order());
    }

    [PostgresFact]
    public async Task Chunks_are_counted_per_document_and_deleted_with_the_document_text()
    {
        var c = MakeClinic();
        var d1 = Doc(c.Id, "d1"); var d2 = Doc(c.Id, "d2");
        await SeedAsync(c, d1, d2);
        // The embedding column is NOT NULL: seed through raw SQL with a value of whichever type this server's schema uses
        // (vector with pgvector, real[] without).
        var vector = await HasVectorAsync();
        async Task InsertChunkAsync(KnowledgeDocument d, int index)
        {
            await using var seed = NewContext();
            var embedding = vector ? $"('[' || array_to_string(array_fill(0.1::real, ARRAY[1536]), ',') || ']')::vector" : "array_fill(0.1::real, ARRAY[3])";
            await seed.Database.ExecuteSqlRawAsync(
                $"insert into knowledge.knowledge_chunks (id, clinic_id, knowledge_document_id, chunk_index, content, embedding, created_at, updated_at) values ('{Guid.NewGuid()}', '{c.Id}', '{d.Id}', {index}, 'chunk {index}', {embedding}, now(), now())");
        }
        await InsertChunkAsync(d1, 0); await InsertChunkAsync(d1, 1); await InsertChunkAsync(d2, 0);

        await using var db = NewContext();
        var repo = new KnowledgeDocumentRepository(db);
        var counts = await repo.CountChunksByDocumentAsync(c.Id);
        Assert.Equal((2, 1), (counts[d1.Id], counts[d2.Id]));
        Assert.Equal(2, await repo.CountChunksAsync(d1.Id));
        await repo.DeleteChunksAsync(d1.Id);
        Assert.Equal(0, await repo.CountChunksAsync(d1.Id));
        Assert.Equal(1, await repo.CountChunksAsync(d2.Id));
    }

    [PostgresFact]
    public async Task Inserting_chunks_validates_input_and_stores_batches_when_pgvector_is_available()
    {
        var c = MakeClinic();
        var d = Doc(c.Id, "d");
        await SeedAsync(c, d);

        await using var db = NewContext();
        var repo = new KnowledgeDocumentRepository(db);
        await Assert.ThrowsAsync<ArgumentException>(() => repo.InsertChunksAsync(c.Id, d.Id, ["a", "b"], [new float[] { 1f }], Now));

        // The ::vector cast needs the pgvector extension, which a plain local Postgres lacks.
        if (!await HasVectorAsync()) return;

        var pieces = Enumerable.Range(0, 450).Select(i => "piece " + i).ToList();
        var vectors = pieces.Select((_, i) => new float[1536].Select((_, j) => j == i % 1536 ? 1f : 0f).ToArray()).ToList();
        await repo.InsertChunksAsync(c.Id, d.Id, pieces, vectors, Now);
        Assert.Equal(450, await repo.CountChunksAsync(d.Id));

        var hits = await repo.SearchChunksAsync(c.Id, vectors[7], 3);
        Assert.Equal("piece 7", hits[0].Content);
        Assert.Equal(3, hits.Count);
    }
}

public class WebsiteSourceRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static KnowledgeWebsiteSource Source(Guid clinicId, string url, int daysAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClinicId = clinicId, StartUrl = url, NormalizedStartUrl = url, Host = "example.com",
        CreatedAt = Now.AddDays(-daysAgo), UpdatedAt = Now,
    };

    private static KnowledgeWebsiteScrapeRun Run(KnowledgeWebsiteSource s, string status, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid(), ClinicId = s.ClinicId, WebsiteSourceId = s.Id, Status = status, CreatedAt = Now.AddMinutes(-minutesAgo),
    };

    private static KnowledgeWebsitePage Page(KnowledgeWebsiteSource s, string url, string status, int depth = 0, Guid? documentId = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = s.ClinicId, WebsiteSourceId = s.Id, Url = url, NormalizedUrl = url, Status = status, Depth = depth,
        KnowledgeDocumentId = documentId, FirstDiscoveredAt = Now, CreatedAt = Now, UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Sources_are_counted_listed_and_found_by_url()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var older = Source(c.Id, "https://a.example/", 2); var newer = Source(c.Id, "https://b.example/", 1); var foreign = Source(other.Id, "https://a.example/");
        await SeedAsync(c, other, older, newer, foreign);

        await using var db = NewContext();
        var repo = new WebsiteSourceRepository(db);
        Assert.Equal(2, await repo.CountSourcesAsync(c.Id));
        Assert.True(await repo.SourceUrlExistsAsync(c.Id, "https://a.example/"));
        Assert.False(await repo.SourceUrlExistsAsync(c.Id, "https://zzz.example/"));
        Assert.Equal([newer.Id, older.Id], (await repo.ListSourcesReadOnlyAsync(c.Id)).Select(s => s.Id));
        Assert.NotNull(await repo.GetSourceReadOnlyAsync(c.Id, older.Id));
        Assert.Null(await repo.GetSourceReadOnlyAsync(other.Id, older.Id));
        Assert.NotNull(await repo.GetSourceAsync(c.Id, older.Id));
        Assert.Null(await repo.GetSourceAsync(other.Id, older.Id));
        Assert.NotNull(await repo.GetSourceByIdAsync(older.Id));

        var added = Source(c.Id, "https://c.example/");
        repo.AddSource(added);
        await db.SaveChangesAsync();
        repo.RemoveSource((await repo.GetSourceByIdAsync(added.Id))!);
        await db.SaveChangesAsync();
        Assert.Equal(2, await new WebsiteSourceRepository(NewContext()).CountSourcesAsync(c.Id));
    }

    [PostgresFact]
    public async Task Reload_discards_unsaved_changes()
    {
        var c = MakeClinic();
        var s = Source(c.Id, "https://reload.example/");
        await SeedAsync(c, s);
        await using var db = NewContext();
        var repo = new WebsiteSourceRepository(db);
        var tracked = (await repo.GetSourceAsync(c.Id, s.Id))!;
        tracked.Status = WebsiteScrapeStatus.Failed;
        await repo.ReloadAsync(tracked);
        Assert.Equal(WebsiteScrapeStatus.Pending, tracked.Status);
    }

    [PostgresFact]
    public async Task Runs_are_listed_by_source_and_status()
    {
        var c = MakeClinic(); var other = MakeClinic();
        var s1 = Source(c.Id, "https://s1.example/"); var s2 = Source(c.Id, "https://s2.example/");
        var done = Run(s1, WebsiteScrapeStatus.Completed, 30);
        var pending = Run(s1, WebsiteScrapeStatus.Pending, 20);
        var crawling = Run(s2, WebsiteScrapeStatus.Crawling, 10);
        await SeedAsync(c, other, s1, s2, done, pending, crawling);

        await using var db = NewContext();
        var repo = new WebsiteSourceRepository(db);
        Assert.Equal(done.Id, (await repo.GetRunAsync(done.Id))!.Id);
        Assert.Equal(2, (await repo.ListRunsReadOnlyAsync(c.Id, [s1.Id])).Count);
        Assert.Empty(await repo.ListRunsReadOnlyAsync(other.Id, [s1.Id]));
        Assert.Equal([pending.Id, done.Id], (await repo.ListRecentRunsReadOnlyAsync(c.Id, s1.Id, 5)).Select(r => r.Id));
        Assert.Single(await repo.ListRecentRunsReadOnlyAsync(c.Id, s1.Id, 1));
        Assert.Equal(pending.Id, (await repo.GetLatestRunReadOnlyAsync(s1.Id))!.Id);
        Assert.True(await repo.HasRunInProgressAsync(s1.Id));
        Assert.True(await repo.HasRunInProgressAsync(s2.Id));
        Assert.Contains(crawling.Id, (await repo.ListRunsInStatusAsync(WebsiteScrapeStatus.Crawling)).Select(r => r.Id));
        Assert.Contains(pending.Id, await repo.ListRunIdsInStatusAsync(WebsiteScrapeStatus.Pending));

        var added = Run(s2, WebsiteScrapeStatus.Pending);
        repo.AddRun(added);
        await db.SaveChangesAsync();
        Assert.NotNull(await new WebsiteSourceRepository(NewContext()).GetRunAsync(added.Id));
    }

    [PostgresFact]
    public async Task A_source_with_only_finished_runs_has_nothing_in_progress()
    {
        var c = MakeClinic();
        var s = Source(c.Id, "https://idle.example/");
        await SeedAsync(c, s, Run(s, WebsiteScrapeStatus.Completed));
        await using var db = NewContext();
        Assert.False(await new WebsiteSourceRepository(db).HasRunInProgressAsync(s.Id));
    }

    [PostgresFact]
    public async Task Pages_are_mapped_listed_counted_and_resolved_to_documents()
    {
        var c = MakeClinic();
        var s = Source(c.Id, "https://pages.example/");
        var d1 = new KnowledgeDocument { Id = Guid.NewGuid(), ClinicId = c.Id, Title = "d1", Content = "x", CreatedAt = Now, UpdatedAt = Now };
        var d2 = new KnowledgeDocument { Id = Guid.NewGuid(), ClinicId = c.Id, Title = "d2", Content = "x", CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(c, s, d1, d2);
        var p1 = Page(s, "https://pages.example/", WebsitePageStatus.Indexed, 0, d1.Id);
        var p2 = Page(s, "https://pages.example/a", WebsitePageStatus.Unchanged, 1, d2.Id);
        var p3 = Page(s, "https://pages.example/b", WebsitePageStatus.Failed, 1);
        var p4 = Page(s, "https://pages.example/c", WebsitePageStatus.Discovered, 2);
        await SeedAsync(p1, p2, p3, p4);

        await using var db = NewContext();
        var repo = new WebsiteSourceRepository(db);
        var map = await repo.MapPagesByUrlAsync(c.Id, s.Id);
        Assert.Equal(4, map.Count);
        Assert.Equal(p2.Id, map["https://pages.example/a"].Id);

        Assert.Equal(["https://pages.example/", "https://pages.example/a", "https://pages.example/b", "https://pages.example/c"],
            (await repo.ListPagesReadOnlyAsync(c.Id, s.Id, null, 0, 10)).Select(p => p.NormalizedUrl));
        Assert.Equal([p3.Id], (await repo.ListPagesReadOnlyAsync(c.Id, s.Id, WebsitePageStatus.Failed, 0, 10)).Select(p => p.Id));
        Assert.Equal([p2.Id], (await repo.ListPagesReadOnlyAsync(c.Id, s.Id, null, 1, 1)).Select(p => p.Id));

        var byStatus = await repo.CountPagesByStatusAsync(c.Id, s.Id);
        Assert.Equal((1, 1, 1, 1), (byStatus[WebsitePageStatus.Indexed], byStatus[WebsitePageStatus.Unchanged], byStatus[WebsitePageStatus.Failed], byStatus[WebsitePageStatus.Discovered]));
        Assert.Equal(2, (await repo.CountIndexedPagesAsync(c.Id, [s.Id]))[s.Id]);

        Assert.Equivalent(new[] { d1.Id, d2.Id }, await repo.ListPageDocumentIdsAsync(c.Id, s.Id, true));
        Assert.Equivalent(new[] { d1.Id, d2.Id }, await repo.ListPageDocumentIdsAsync(c.Id, s.Id, false));

        repo.AddPage(Page(s, "https://pages.example/d", WebsitePageStatus.Discovered));
        await db.SaveChangesAsync();
        Assert.Equal(5, (await new WebsiteSourceRepository(NewContext()).MapPagesByUrlAsync(c.Id, s.Id)).Count);
    }
}
