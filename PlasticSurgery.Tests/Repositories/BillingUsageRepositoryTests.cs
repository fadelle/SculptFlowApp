using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Repositories.Billing;

namespace PlasticSurgery.Tests.Repositories;

/// <summary>The usage queries behind the clinic's billing page, the admin portal and the monthly report.</summary>
public class BillingUsageRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    // Real time on purpose: the billing sweeper treats old reserved/pending rows as stale, and those queries are global, so
    // rows seeded in the past would leak into the other billing tests that share this database.
    private new static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed record Seeded(Clinic Clinic, BillingAccount Account);

    private async Task<Seeded> SeedAccountAsync()
    {
        var c = MakeClinic();
        var account = new BillingAccount { Id = Guid.NewGuid(), ClinicId = c.Id, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(c, account);
        return new Seeded(c, account);
    }

    private static BillingUsageRecord Usage(Seeded s, string eventType, string chargeStatus, string outcome, decimal? amount = null, decimal quantity = 1,
        DateTimeOffset? at = null, string channel = "whatsapp", decimal? providerCost = null, decimal refunded = 0, decimal reserved = 0,
        string providerBilling = ProviderBillingResponsibility.PlatformFunded) => new()
    {
        Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, BillingAccountId = s.Account.Id, IdempotencyKey = Guid.NewGuid().ToString("N"), EventType = eventType,
        Channel = channel, Quantity = quantity, ChargeStatus = chargeStatus, ProviderOutcome = outcome, Amount = amount, ProviderCost = providerCost,
        RefundedAmount = refunded, ReservedAmount = reserved, WalletAmount = chargeStatus == ChargeStatus.Settled ? amount ?? 0 : 0, ProviderBilling = providerBilling, OccurredAt = at ?? Now, CreatedAt = at ?? Now, UpdatedAt = Now,
    };

    [PostgresFact]
    public async Task Usage_is_found_by_key_and_id_within_the_clinic()
    {
        var s = await SeedAccountAsync();
        var u = Usage(s, "sms", ChargeStatus.Settled, ProviderOutcome.Billable, 1m);
        await SeedAsync(u);

        await using var db = NewContext();
        var repo = new BillingUsageRepository(db);
        Assert.Equal(u.Id, (await repo.FindByKeyAsync(s.Clinic.Id, u.IdempotencyKey))!.Id);
        Assert.Equal(u.Id, (await repo.FindByKeyReadOnlyAsync(s.Clinic.Id, u.IdempotencyKey))!.Id);
        Assert.Null(await repo.FindByKeyAsync(Guid.NewGuid(), u.IdempotencyKey));
        Assert.NotNull(await repo.GetAsync(s.Clinic.Id, u.Id));
        Assert.Null(await repo.GetAsync(Guid.NewGuid(), u.Id));

        var added = Usage(s, "sms", ChargeStatus.Reserved, ProviderOutcome.Pending);
        repo.Add(added);
        await db.SaveChangesAsync();
        Assert.NotNull(await new BillingUsageRepository(NewContext()).GetAsync(s.Clinic.Id, added.Id));
    }

    [PostgresFact]
    public async Task Stale_usage_is_reserved_or_uncharged_and_still_pending()
    {
        var s = await SeedAccountAsync();
        var old = Now.AddDays(-3);
        var staleReserved = Usage(s, "sms", ChargeStatus.Reserved, ProviderOutcome.Pending, at: old);
        var stalePending = Usage(s, "sms", ChargeStatus.NotCharged, ProviderOutcome.Pending, at: old.AddMinutes(1));
        var settled = Usage(s, "sms", ChargeStatus.Settled, ProviderOutcome.Billable, 1m, at: old);
        var fresh = Usage(s, "sms", ChargeStatus.Reserved, ProviderOutcome.Pending, at: Now);
        await SeedAsync(staleReserved, stalePending, settled, fresh);

        await using var db = NewContext();
        try
        {
        var stale = await new BillingUsageRepository(db).ListStaleReadOnlyAsync(Now.AddDays(-1), 100);
        Assert.Contains(staleReserved.Id, stale.Select(x => x.Id));
        Assert.Contains(stalePending.Id, stale.Select(x => x.Id));
        Assert.DoesNotContain(settled.Id, stale.Select(x => x.Id));
        Assert.DoesNotContain(fresh.Id, stale.Select(x => x.Id));
        Assert.Single(await new BillingUsageRepository(db).ListStaleReadOnlyAsync(Now.AddDays(-1), 1));
        }
        finally
        {
            // leave nothing stale behind for the other billing tests
            await using var cleanup = NewContext();
            await cleanup.BillingUsageRecords.Where(u => u.ClinicId == s.Clinic.Id).ExecuteDeleteAsync();
        }
    }

    [PostgresFact]
    public async Task Clinic_and_admin_lists_filter_and_page()
    {
        var s = await SeedAccountAsync();
        var sms = Usage(s, "sms", ChargeStatus.Settled, ProviderOutcome.Billable, 1m, at: Now.AddDays(-3));
        var template = Usage(s, "template", ChargeStatus.Reserved, ProviderOutcome.Pending, at: Now.AddDays(-2));
        var failed = Usage(s, "sms", ChargeStatus.Failed, ProviderOutcome.NotBillable, at: Now.AddDays(-1));
        await SeedAsync(sms, template, failed);

        await using var db = NewContext();
        var repo = new BillingUsageRepository(db);

        var (clinicRows, clinicTotal) = await repo.ListClinicUsageAsync(s.Clinic.Id, 0, 10);
        Assert.Equal(2, clinicTotal);                                  // failed rows are hidden from the clinic
        Assert.Equal([template.Id, sms.Id], clinicRows.Select(r => r.Id));
        Assert.Single((await repo.ListClinicUsageAsync(s.Clinic.Id, 1, 10)).Items);

        var all = await repo.ListAdminUsageAsync(s.Clinic.Id, null, null, null, null, 0, 10);
        Assert.Equal(3, all.Total);
        Assert.Equal([failed.Id], (await repo.ListAdminUsageAsync(s.Clinic.Id, ChargeStatus.Failed, null, null, null, 0, 10)).Items.Select(r => r.Id));
        Assert.Equal(2, (await repo.ListAdminUsageAsync(s.Clinic.Id, null, "sms", null, null, 0, 10)).Total);
        Assert.Equal([template.Id], (await repo.ListAdminUsageAsync(s.Clinic.Id, null, null, Now.AddDays(-2), Now.AddDays(-1), 0, 10)).Items.Select(r => r.Id));
        Assert.Single((await repo.ListAdminUsageAsync(s.Clinic.Id, null, null, null, null, 2, 10)).Items);
    }

    [PostgresFact]
    public async Task Reserved_totals_and_the_usage_groupings()
    {
        var s = await SeedAccountAsync();
        var since = Now.AddDays(-10);
        await SeedAsync(
            Usage(s, "sms", ChargeStatus.Reserved, ProviderOutcome.Pending, reserved: 0.5m),
            Usage(s, "sms", ChargeStatus.Reserved, ProviderOutcome.Pending, reserved: 0.25m),
            Usage(s, "sms", ChargeStatus.Settled, ProviderOutcome.Billable, 2m, quantity: 2, providerCost: 1m, refunded: 0.5m),
            Usage(s, "sms", ChargeStatus.Settled, ProviderOutcome.Billable, 3m, quantity: 1, providerCost: 1m),
            Usage(s, "template", ChargeStatus.NotCharged, ProviderOutcome.Billable, null, channel: "telegram", providerBilling: ProviderBillingResponsibility.CustomerDirect),
            Usage(s, "template", ChargeStatus.NotCharged, ProviderOutcome.Pending, null, channel: "telegram", providerBilling: ProviderBillingResponsibility.CustomerDirect),
            Usage(s, "old", ChargeStatus.Settled, ProviderOutcome.Billable, 9m, at: Now.AddDays(-60)));

        await using var db = NewContext();
        var repo = new BillingUsageRepository(db);

        Assert.Equal(2, await repo.CountReservedAsync(s.Clinic.Id));
        Assert.Equal(0.75m, await repo.SumReservedAsync(s.Clinic.Id));

        var settled = Assert.Single(await repo.GroupSettledAsync(s.Clinic.Id, since));
        Assert.Equal(("sms", 2, 3m, 4.5m), (settled.EventType, settled.Count, settled.Quantity, settled.Amount));   // (2-0.5)+3

        var uncharged = await repo.GroupUnchargedAsync(s.Clinic.Id, since);
        var group = Assert.Single(uncharged);
        Assert.Equal(("template", "telegram", 2), (group.EventType, group.Channel, group.Count));

        var billable = await repo.GroupBillableAsync(s.Clinic.Id, since);
        var sms = billable.Single(b => b.EventType == "sms");
        Assert.Equal((2, 4.5m, 2m), (sms.Count, sms.Amount, sms.Cost));
        Assert.Contains(billable, b => b.EventType == "template" && b.Amount == 0m);

        var report = await repo.GroupBillableForReportAsync(since, Now.AddDays(1));
        var row = report.Single(r => r.ClinicId == s.Clinic.Id && r.EventType == "sms");
        Assert.Equal((2, 4.5m, 2m), (row.Count, row.Revenue, row.Cost));
        Assert.DoesNotContain(report, r => r.ClinicId == s.Clinic.Id && r.EventType == "old");
    }
}
