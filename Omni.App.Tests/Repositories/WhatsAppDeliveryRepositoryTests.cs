using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Channels;

namespace Omni.App.Tests.Repositories;

/// <summary>The WhatsApp webhook inbox on Postgres (QA t10-10): stored once per body, claimed oldest first with SKIP LOCKED,
/// a lapsed claim taken again, retries wait, done rows cleaned up; one WhatsApp conversation per contact.</summary>
public class WhatsAppDeliveryRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private async Task ClearAsync()
    {
        await using var db = NewContext();
        await db.WhatsAppWebhookDeliveries.ExecuteDeleteAsync();
    }

    [PostgresFact]
    public async Task Deliveries_are_stored_once_claimed_in_order_and_finished()
    {
        await ClearAsync();
        await using (var db = NewContext())
        {
            var repo = new WhatsAppDeliveryRepository(db);
            Assert.True(await repo.EnqueueAsync("{\"n\":1}", "h1", "00-trace-span-01", Now));
            Assert.False(await repo.EnqueueAsync("{\"n\":1}", "h1", null, Now)); // Meta redelivered it
            Assert.True(await repo.EnqueueAsync("{\"n\":2}", "h2", null, Now));
        }

        // Two workers at once: each gets a different delivery, the oldest first.
        await using var a = NewContext();
        await using var b = NewContext();
        await using var tx = await a.Database.BeginTransactionAsync();
        var first = await new WhatsAppDeliveryRepository(a).ClaimNextAsync(Now, Now.AddMinutes(5));
        var second = await new WhatsAppDeliveryRepository(b).ClaimNextAsync(Now, Now.AddMinutes(5));
        await tx.CommitAsync();
        Assert.Equal(("{\"n\":1}", "00-trace-span-01", 1, "processing"), (first!.Body, first.TraceParent, first.Attempts, first.Status));
        Assert.Equal("{\"n\":2}", second!.Body);
        Assert.Null(await new WhatsAppDeliveryRepository(b).ClaimNextAsync(Now, Now.AddMinutes(5))); // both are claimed

        var repoB = new WhatsAppDeliveryRepository(b);
        await repoB.CompleteAsync(first.Id, Now);
        await repoB.RetryLaterAsync(second.Id, "db down", Now.AddSeconds(10));
        Assert.Null(await repoB.ClaimNextAsync(Now.AddSeconds(5), Now.AddMinutes(5)));          // not due yet
        var again = await repoB.ClaimNextAsync(Now.AddSeconds(11), Now.AddMinutes(5));
        Assert.Equal((second.Id, 2), (again!.Id, again.Attempts));
        await repoB.FailAsync(again.Id, "gave up", Now.AddSeconds(12));

        await using var read = NewContext();
        var rows = await read.WhatsAppWebhookDeliveries.AsNoTracking().OrderBy(d => d.Id).ToListAsync();
        Assert.Equal([("done", (string?)null), ("failed", "gave up")], rows.Select(r => (r.Status, r.LastError)));
        Assert.Equal(0, await repoB.DeleteDoneBeforeAsync(Now));                     // processed at Now: not before Now
        Assert.Equal(1, await repoB.DeleteDoneBeforeAsync(Now.AddDays(8)));          // the failed one is kept for a person
        Assert.Equal("failed", Assert.Single(await read.WhatsAppWebhookDeliveries.AsNoTracking().ToListAsync()).Status);
    }

    [PostgresFact]
    public async Task A_claim_left_by_a_crashed_worker_is_taken_again_when_it_lapses()
    {
        await ClearAsync();
        await using var db = NewContext();
        var repo = new WhatsAppDeliveryRepository(db);
        await repo.EnqueueAsync("{}", "lapse", null, Now);
        var claimed = await repo.ClaimNextAsync(Now, Now.AddMinutes(5));
        Assert.Null(await repo.ClaimNextAsync(Now.AddMinutes(4), Now.AddMinutes(9)));   // still held
        var retaken = await repo.ClaimNextAsync(Now.AddMinutes(6), Now.AddMinutes(11));
        Assert.Equal((claimed!.Id, 2), (retaken!.Id, retaken.Attempts));
    }

    [PostgresFact]
    public async Task A_contact_has_one_whatsapp_conversation_but_may_have_one_per_other_channel()
    {
        var c = MakeClient("One conversation");
        var contact = new Contact { Id = Guid.NewGuid(), ClientId = c.Id, FullName = "Ann", StageKey = "new", CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(c, contact);
        Conversation Conv(string channel) => new()
        {
            Id = Guid.NewGuid(), ClientId = c.Id, ContactId = contact.Id, Channel = channel, Status = "active", Mode = "human",
            CreatedAt = Now, UpdatedAt = Now
        };
        await SeedAsync(Conv("whatsapp"), Conv("telegram"));
        await using var db = NewContext();
        db.Conversations.Add(Conv("whatsapp"));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
