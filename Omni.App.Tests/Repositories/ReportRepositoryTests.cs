using Omni.App.Entities.Dtos.Reports;
using Omni.App.Persistence.Repositories.Reports;

namespace Omni.App.Tests.Repositories;

/// <summary>The Reports page's queries: each counts only its own client and only inside [from, to).</summary>
public class ReportRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static readonly DateTimeOffset From = Now.AddDays(-1);
    private static readonly DateTimeOffset To = Now.AddDays(1);

    private static Message Out(Conversation c, string origin, DateTimeOffset at)
    {
        var m = MakeMessage(c, MessageDirection.Outbound, origin, createdAt: at);
        m.SenderType = origin switch
        {
            MessageOrigin.Ai => MessageSenderType.Ai,
            MessageOrigin.Dashboard or MessageOrigin.WhatsAppBusinessApp => MessageSenderType.Member,
            _ => MessageSenderType.System,
        };
        return m;
    }

    private static Conversation Conversation(Guid clientId, Guid contactId, string channel, DateTimeOffset createdAt)
    {
        var c = MakeConversation(clientId, contactId, channel);
        c.CreatedAt = createdAt;
        return c;
    }

    [PostgresFact]
    public async Task Channel_activity_counts_conversations_and_messages_per_channel_in_range()
    {
        var c = MakeClient();
        var other = MakeClient();
        var ann = MakeContact(c.Id);
        var bob = MakeContact(c.Id, "Bob");
        var stranger = MakeContact(other.Id);
        await SeedAsync(c, other, ann, bob, stranger);
        var wa = Conversation(c.Id, ann.Id, "whatsapp", Now);
        var tg = Conversation(c.Id, bob.Id, "telegram", Now.AddDays(-5));          // started before the range
        var foreign = Conversation(other.Id, stranger.Id, "whatsapp", Now);
        await SeedAsync(wa, tg, foreign);
        await SeedAsync(
            MakeMessage(wa, createdAt: Now), Out(wa, MessageOrigin.Ai, Now.AddMinutes(1)), Out(wa, MessageOrigin.Campaign, Now.AddMinutes(2)),
            MakeMessage(tg, origin: MessageOrigin.TelegramCustomer, createdAt: Now),
            MakeMessage(tg, origin: MessageOrigin.TelegramCustomer, createdAt: Now.AddDays(-5)),   // out of range
            MakeMessage(foreign, createdAt: Now));

        var rows = await new ReportRepository(NewContext()).GetChannelActivityAsync(c.Id, From, To);

        Assert.Equal(
            [new ReportChannelRow("whatsapp", 1, 1, 2), new ReportChannelRow("telegram", 0, 1, 0)],
            rows);
        var allTime = await new ReportRepository(NewContext()).GetChannelActivityAsync(c.Id, null, null);
        Assert.Equal(new ReportChannelRow("telegram", 1, 2, 0), allTime.Single(r => r.Channel == "telegram"));
    }

    [PostgresFact]
    public async Task New_contacts_are_counted_by_source_and_by_stage()
    {
        var c = MakeClient();
        var a = MakeContact(c.Id, "A"); a.Source = "instagram";
        var b = MakeContact(c.Id, "B"); b.Source = "instagram";
        var d = MakeContact(c.Id, "D", stage: PipelineKit.Booked);
        var old = MakeContact(c.Id, "Old", createdAt: Now.AddDays(-9)); old.Source = "website";
        await SeedAsync(c, a, b, d, old);

        var repo = new ReportRepository(NewContext());
        Assert.Equal([new ReportCountRow("instagram", 2), new ReportCountRow(null, 1)], await repo.CountNewContactsBySourceAsync(c.Id, From, To));
        var stages = await repo.CountNewContactsByStageAsync(c.Id, From, To);
        Assert.Equal([(PipelineKit.New, 2), (PipelineKit.Booked, 1)], stages.Select(s => (s.Key, s.Count)));
    }

    [PostgresFact]
    public async Task Bookings_are_made_by_created_date_and_outcomes_by_scheduled_start()
    {
        var c = MakeClient();
        var ann = MakeContact(c.Id);
        await SeedAsync(c, ann);
        var attended = MakeBooking(c.Id, ann.Id, Now, BookingStatus.Attended);
        var noShow = MakeBooking(c.Id, ann.Id, Now.AddHours(2), BookingStatus.NoShow);
        var canceled = MakeBooking(c.Id, ann.Id, Now.AddHours(3), BookingStatus.Canceled);
        var later = MakeBooking(c.Id, ann.Id, Now.AddDays(10), BookingStatus.Attended);   // made now, happens later
        var older = MakeBooking(c.Id, ann.Id, Now.AddHours(4), BookingStatus.Booked);
        older.CreatedAt = Now.AddDays(-20);
        await SeedAsync(attended, noShow, canceled, later, older);

        var counts = await new ReportRepository(NewContext()).GetBookingCountsAsync(c.Id, From, To);
        Assert.Equal(new ReportBookingCounts(4, 1, 1, 1), counts);
    }

    [PostgresFact]
    public async Task Campaigns_that_started_in_range_show_how_far_their_recipients_got()
    {
        var c = MakeClient();
        var ann = MakeContact(c.Id);
        var bob = MakeContact(c.Id, "Bob");
        var cy = MakeContact(c.Id, "Cy");
        var template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = c.Id, Name = "promo", Body = "b", Status = WhatsAppTemplateStatus.Approved, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(c, ann, bob, cy, template);
        Campaign Make(string name, DateTimeOffset? started) => new()
        {
            Id = Guid.NewGuid(), ClientId = c.Id, Name = name, WhatsAppTemplateId = template.Id, Status = CampaignStatus.Completed,
            StartedAt = started, CreatedAt = Now, UpdatedAt = Now,
        };
        var spring = Make("Spring", Now);
        var summer = Make("Summer", Now.AddHours(1));
        var draft = Make("Draft", null);
        var old = Make("Old", Now.AddDays(-30));
        await SeedAsync(spring, summer, draft, old);
        CampaignRecipient Recipient(Campaign k, Contact who, string status, bool sent = false, bool delivered = false, bool read = false, bool replied = false) => new()
        {
            Id = Guid.NewGuid(), ClientId = c.Id, CampaignId = k.Id, ContactId = who.Id, PhoneNumber = "+9611", Status = status,
            SentAt = sent ? Now : null, DeliveredAt = delivered ? Now : null, ReadAt = read ? Now : null, RepliedAt = replied ? Now : null,
            CreatedAt = Now, UpdatedAt = Now,
        };
        await SeedAsync(
            Recipient(spring, ann, CampaignRecipientStatus.Replied, sent: true, read: true, replied: true),   // read without a delivery receipt
            Recipient(spring, bob, CampaignRecipientStatus.Delivered, sent: true, delivered: true),
            Recipient(spring, cy, CampaignRecipientStatus.Skipped),
            Recipient(summer, ann, CampaignRecipientStatus.Failed));

        var rows = await new ReportRepository(NewContext()).ListCampaignsAsync(c.Id, From, To, 10);

        Assert.Equal(["Summer", "Spring"], rows.Select(r => r.Name));
        var s = rows[1];
        Assert.Equal((2, 2, 2, 1, 1, 0), (s.Recipients, s.Sent, s.Delivered, s.Read, s.Replied, s.Failed));
        Assert.Equal(1, rows[0].Failed);
        Assert.Single(await new ReportRepository(NewContext()).ListCampaignsAsync(c.Id, From, To, 1));
    }

    [PostgresFact]
    public async Task A_wait_starts_at_the_first_message_after_a_reply_and_ends_at_the_next_reply()
    {
        var c = MakeClient();
        var ann = MakeContact(c.Id);
        var bob = MakeContact(c.Id, "Bob");
        await SeedAsync(c, ann, bob);
        var wa = Conversation(c.Id, ann.Id, "whatsapp", Now);
        var tg = Conversation(c.Id, bob.Id, "telegram", Now);
        await SeedAsync(wa, tg);
        await SeedAsync(
            MakeMessage(wa, createdAt: Now),                                      // wait 1 …
            Out(wa, MessageOrigin.Campaign, Now.AddMinutes(1)),                   // (a campaign message is no reply)
            Out(wa, MessageOrigin.Ai, Now.AddMinutes(2)),                         // … answered by the AI after 2 min
            MakeMessage(wa, createdAt: Now.AddMinutes(10)),                       // wait 2 …
            MakeMessage(wa, createdAt: Now.AddMinutes(11)),                       // (joins wait 2)
            Out(wa, MessageOrigin.Dashboard, Now.AddMinutes(40)),                 // … answered by the team after 30 min
            MakeMessage(tg, origin: MessageOrigin.TelegramCustomer, createdAt: Now.AddMinutes(5)),   // wait 3, unanswered
            MakeMessage(wa, createdAt: Now.AddDays(-3)),                          // a wait before the range …
            Out(wa, MessageOrigin.Ai, Now.AddDays(-3).AddMinutes(1)));            // … answered then

        var waits = await new ReportRepository(NewContext()).ListWaitsAsync(c.Id, From, To);

        Assert.Equal(
            [
                new ReportWait(Now, Now.AddMinutes(2), true),
                new ReportWait(Now.AddMinutes(5), null, false),
                new ReportWait(Now.AddMinutes(10), Now.AddMinutes(40), false),
            ],
            waits.OrderBy(w => w.WaitingSince));
    }

    [PostgresFact]
    public async Task Replies_are_split_between_the_ai_and_the_team()
    {
        var c = MakeClient();
        var ann = MakeContact(c.Id);
        var bob = MakeContact(c.Id, "Bob");
        await SeedAsync(c, ann, bob);
        var aiOnly = Conversation(c.Id, ann.Id, "whatsapp", Now);
        var mixed = Conversation(c.Id, bob.Id, "telegram", Now);
        await SeedAsync(aiOnly, mixed);
        await SeedAsync(
            Out(aiOnly, MessageOrigin.Ai, Now), Out(aiOnly, MessageOrigin.Ai, Now.AddMinutes(1)),
            Out(mixed, MessageOrigin.Ai, Now), Out(mixed, MessageOrigin.WhatsAppBusinessApp, Now.AddMinutes(1)),
            Out(mixed, MessageOrigin.Dashboard, Now.AddMinutes(2)), Out(mixed, MessageOrigin.System, Now.AddMinutes(3)),
            Out(mixed, MessageOrigin.Ai, Now.AddDays(-4)));

        var counts = await new ReportRepository(NewContext()).GetReplyCountsAsync(c.Id, From, To);
        Assert.Equal(new ReportReplyCounts(3, 2, 2, 1), counts);
    }

    [PostgresFact]
    public async Task Messages_are_bucketed_in_quarter_hours_of_utc()
    {
        var c = MakeClient();
        var ann = MakeContact(c.Id);
        await SeedAsync(c, ann);
        var wa = Conversation(c.Id, ann.Id, "whatsapp", Now);
        await SeedAsync(wa);
        await SeedAsync(
            MakeMessage(wa, createdAt: Now.AddMinutes(3)), MakeMessage(wa, createdAt: Now.AddMinutes(14)),
            Out(wa, MessageOrigin.Ai, Now.AddMinutes(16)), Out(wa, MessageOrigin.Ai, Now.AddHours(3).AddMinutes(45)));

        var buckets = await new ReportRepository(NewContext()).GetMessageBucketsAsync(c.Id, From, To);
        Assert.Equal(
            [
                new ReportMessageBucket(Now, 2, 0),
                new ReportMessageBucket(Now.AddMinutes(15), 0, 1),
                new ReportMessageBucket(Now.AddHours(3).AddMinutes(45), 0, 1),
            ],
            buckets);
    }

    [PostgresFact]
    public async Task Settled_usage_in_range_is_grouped_by_event_type()
    {
        var c = MakeClient();
        var account = new BillingAccount { Id = Guid.NewGuid(), ClientId = c.Id, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(c, account);
        // Real time: global billing sweepers treat old reserved rows as stale (see BillingUsageRepositoryTests).
        var now = DateTimeOffset.UtcNow;
        BillingUsageRecord Usage(string type, string status, decimal amount, DateTimeOffset at, decimal refunded = 0) => new()
        {
            Id = Guid.NewGuid(), ClientId = c.Id, BillingAccountId = account.Id, IdempotencyKey = Guid.NewGuid().ToString("N"), EventType = type,
            Channel = "whatsapp", Quantity = 1, ChargeStatus = status, ProviderOutcome = ProviderOutcome.Billable, Amount = amount,
            RefundedAmount = refunded, WalletAmount = status == ChargeStatus.Settled ? amount : 0, OccurredAt = at, CreatedAt = at, UpdatedAt = now,
        };
        await SeedAsync(
            Usage("ai_reply", ChargeStatus.Settled, 0.02m, now), Usage("ai_reply", ChargeStatus.Settled, 0.02m, now),
            Usage("whatsapp_marketing_message", ChargeStatus.Settled, 0.10m, now, refunded: 0.04m),
            Usage("ai_reply", ChargeStatus.Released, 0.02m, now),
            Usage("ai_reply", ChargeStatus.Settled, 0.02m, now.AddDays(-10)));

        var rows = await new ReportRepository(NewContext()).GetSettledUsageAsync(c.Id, now.AddDays(-1), now.AddDays(1));
        Assert.Equal(
            [new ReportUsageRow("whatsapp_marketing_message", "USD", 1, 1, 0.06m), new ReportUsageRow("ai_reply", "USD", 2, 2, 0.04m)],
            rows);
    }
}
