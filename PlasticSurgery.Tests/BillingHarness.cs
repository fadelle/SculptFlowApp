using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Services.Billing;
using PlasticSurgery.Common.Configs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Repositories.Billing;
using PlasticSurgery.Persistence.Repositories.Channels;

namespace PlasticSurgery.Tests;

/// <summary>A clock the tests move by hand (renewals, grace periods, rate versions, reservation timeouts).</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset start) => Now = start;
    public DateTimeOffset Now { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>The billing module wired by hand against the test database, with a manual clock.</summary>
public sealed class BillingHarness
{
    public BillingHarness(string connectionString, bool enabled = true, int graceDays = 7, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString);
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        DbOptions = builder.Options;
        Time = new ManualTimeProvider(new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddHours(12), TimeSpan.Zero));
        // Settings (IConfigManager) as the tests want them; RefreshAsync is never called, so nothing reads config.settings.
        Config = new ConfigManager(null!, NullLogger<ConfigManager>.Instance);
        SetSetting("Billing", "Enabled", enabled ? "true" : "false");
        SetSetting("Billing", "GracePeriodDays", graceDays.ToString());
        SetSetting("Billing", "ReservationTimeoutHours", "72");
        SetSetting("Billing", "Currency", "USD");
        Factory = new BillingUnitOfWorkFactory(DbOptions);
        Billing = new BillingService(Factory, Time, NullLogger<BillingService>.Instance, Config);
        Subscriptions = new SubscriptionService(Factory, Time, NullLogger<SubscriptionService>.Instance, Config);
        Plans = new PlanService(Factory, Time, NullLogger<PlanService>.Instance, Config);
        RateCards = new RateCardService(Factory, Time, NullLogger<RateCardService>.Instance, Config);
        ProviderBilling = new ProviderBillingService(Factory, Microsoft.Extensions.Options.Options.Create(new ProviderBillingOptions()), Time,
            NullLogger<ProviderBillingService>.Instance);
        Queries = new BillingQueryService(Factory, Time, ProviderBilling, Config);
    }

    public ProviderBillingService ProviderBilling { get; }

    /// <summary>The settings the billing code reads (Billing:Enabled, GracePeriodDays, ...), set per test.</summary>
    public ConfigManager Config { get; }

    private readonly Dictionary<(string, string), string> _settings = new();

    public void SetSetting(string section, string key, string value)
    {
        _settings[(section, key)] = value;
        Config.Apply(_settings.Select(s => new ConfigSetting { Section = s.Key.Item1, Key = s.Key.Item2, Value = s.Value }));
    }

    /// <summary>A connected channel account (channel_integrations row) for the clinic.</summary>
    public async Task<Guid> ConnectChannelAsync(Guid clinicId, string channel, string? provider = null)
    {
        await using var db = Db();
        var id = Guid.NewGuid();
        db.ChannelIntegrations.Add(new ChannelIntegration
        {
            Id = id, ClinicId = clinicId, Channel = channel, Provider = provider, Status = ChannelIntegrationStatus.Connected,
            DisplayName = channel + " test", CreatedAt = Time.Now, UpdatedAt = Time.Now
        });
        await db.SaveChangesAsync();
        return id;
    }

    public DbContextOptions<ApplicationDbContext> DbOptions { get; }
    public ManualTimeProvider Time { get; }
    public BillingUnitOfWorkFactory Factory { get; }
    public BillingService Billing { get; }
    public SubscriptionService Subscriptions { get; }
    public PlanService Plans { get; }
    public RateCardService RateCards { get; }
    public BillingQueryService Queries { get; }

    public ApplicationDbContext Db() => new(DbOptions);

    public EntitlementService Entitlements() =>
        Entitlements(Db());

    public EntitlementService Entitlements(ApplicationDbContext db) =>
        new(new ClinicSubscriptionRepository(db), new ChannelIntegrationRepository(db), Time, NullLogger<EntitlementService>.Instance, Config);

    public static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 50)];

    public async Task<Guid> CreateClinicAsync()
    {
        await using var db = Db();
        var id = Guid.NewGuid();
        db.Clinics.Add(new Clinic { Id = id, Name = "Test clinic " + id.ToString("N")[..6], Slug = "test-" + id.ToString("N"), CreatedAt = Time.Now, UpdatedAt = Time.Now });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A clinic-specific rate card (isolates tests from each other and from the global default card).</summary>
    public async Task<string> CreateClientCardAsync(Guid clinicId)
    {
        var code = Unique("client").Replace('_', '-');
        await RateCards.CreateAsync(new RateCardRequest(code, "Client card", null, clinicId));
        return code;
    }

    public Task<RateResponse?> AddRateAsync(string cardCode, string eventType, decimal clientRate, decimal providerCost = 0,
        string? country = null, string? provider = null, string? op = null, DateTimeOffset? from = null, string? providerBilling = null) =>
        RateCards.AddRateAsync(cardCode, new AddRateRequest(eventType, country, op, provider, "message", providerCost, clientRate, from,
            ProviderBilling: providerBilling), "tests");

    public Task<LedgerResult> TopUpAsync(Guid clinicId, decimal amount) =>
        Billing.TopUpAsync(new WalletTopUp(clinicId, amount, Guid.NewGuid().ToString("N"), "test", "test top-up", BillingSource.Admin, "tests"));

    /// <summary>A plan with the given credit and entitlements, unique code.</summary>
    public async Task<string> CreatePlanAsync(decimal price, decimal credit, Dictionary<string, string>? entitlements = null, string period = "month",
        string? rateCardCode = null)
    {
        var code = Unique("plan").Replace('_', '-');
        await Plans.CreateAsync(new PlanRequest(code, "Plan " + code, null, price, period, credit, rateCardCode, true, 0, entitlements));
        return code;
    }

    public BillableEvent Event(Guid clinicId, string eventType, decimal quantity = 1, string? key = null, string? country = null,
        string? provider = null, DateTimeOffset? occurredAt = null) => new()
    {
        ClinicId = clinicId,
        IdempotencyKey = key ?? "test:" + Guid.NewGuid().ToString("N"),
        EventType = eventType,
        Channel = "test",
        Quantity = quantity,
        CountryCode = country,
        Provider = provider,
        OccurredAt = occurredAt
    };

    public async Task<BillingAccount> AccountAsync(Guid clinicId)
    {
        await using var db = Db();
        return await db.BillingAccounts.AsNoTracking().SingleAsync(a => a.ClinicId == clinicId);
    }

    public async Task<List<BillingLedgerEntry>> LedgerAsync(Guid clinicId)
    {
        await using var db = Db();
        return await db.BillingLedgerEntries.AsNoTracking().Where(l => l.ClinicId == clinicId).OrderBy(l => l.Seq).ToListAsync();
    }

    public async Task<BillingUsageRecord> UsageAsync(Guid usageId)
    {
        await using var db = Db();
        return await db.BillingUsageRecords.AsNoTracking().SingleAsync(u => u.Id == usageId);
    }

    /// <summary>The invariant every test ends with: cached balances = ledger sums, reserved = open reservations.</summary>
    public async Task AssertReconciledAsync(Guid clinicId)
    {
        var r = await Queries.ReconcileAsync(clinicId);
        Assert.True(r.IsBalanced,
            $"Not reconciled: wallet {r.WalletBalance} vs ledger {r.WalletLedgerTotal}, credit {r.IncludedCreditBalance} vs {r.IncludedCreditLedgerTotal}, reserved {r.ReservedAmount} vs open {r.OpenReservationsTotal}");

        // The ledger is a chain: in posting order, each entry's balance_after = the previous one + its amount.
        var running = new Dictionary<string, decimal> { [LedgerBalanceType.Wallet] = 0m, [LedgerBalanceType.IncludedCredit] = 0m };
        foreach (var entry in await LedgerAsync(clinicId))
        {
            running[entry.BalanceType] += entry.Amount;
            Assert.Equal(running[entry.BalanceType], entry.BalanceAfter);
        }
    }
}

/// <summary>Null object for notification-style interfaces whose calls the tests don't care about.</summary>
public class NullProxy<T> : DispatchProxy where T : class
{
    public static T Create() => Create<T, NullProxy<T>>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var type = targetMethod!.ReturnType;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type == typeof(void)) return null;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>Simulates the process dying after the billing writes were sent but before the transaction committed.</summary>
public sealed class CrashAfterSaveInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; } = true;

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (Armed)
        {
            Armed = false;
            throw new InvalidOperationException("Simulated crash after SaveChanges, before commit.");
        }
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
