using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

/// <summary>Entitlement keys a plan can set (billing.plan_entitlements.entitlement_key). To add one: add a
/// constant + a definition below, give it a ClinicEntitlements property if code asks it often, check it where the
/// feature lives, and set it on the plans (admin API). No schema change.</summary>
public static class EntitlementKeys
{
    public const string Campaigns = "campaigns";
    /// <summary>Automation: the AI agent answers conversations (n8n trigger).</summary>
    public const string AiAgent = "ai_agent";
    public const string ApiAccess = "api_access";
    public const string AdvancedReporting = "advanced_reporting";
    public const string MaxAgents = "max_agents";
    public const string MaxWhatsAppNumbers = "max_whatsapp_numbers";
    public const string MaxChannelConnections = "max_channel_connections";
}

public enum EntitlementKind { Feature, Limit }

public sealed record EntitlementDefinition(string Key, EntitlementKind Kind, string Label);

public static class EntitlementCatalog
{
    public const string Unlimited = "unlimited";

    public static readonly IReadOnlyList<EntitlementDefinition> All = new[]
    {
        new EntitlementDefinition(EntitlementKeys.Campaigns, EntitlementKind.Feature, "Campaigns"),
        new EntitlementDefinition(EntitlementKeys.AiAgent, EntitlementKind.Feature, "AI agent"),
        new EntitlementDefinition(EntitlementKeys.ApiAccess, EntitlementKind.Feature, "API access"),
        new EntitlementDefinition(EntitlementKeys.AdvancedReporting, EntitlementKind.Feature, "Advanced reporting"),
        new EntitlementDefinition(EntitlementKeys.MaxAgents, EntitlementKind.Limit, "Staff seats"),
        new EntitlementDefinition(EntitlementKeys.MaxWhatsAppNumbers, EntitlementKind.Limit, "WhatsApp numbers"),
        new EntitlementDefinition(EntitlementKeys.MaxChannelConnections, EntitlementKind.Limit, "Connected channels"),
    };

    public static EntitlementDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Normalizes and validates a value for a key: features take true/false, limits a whole number or
    /// "unlimited". Throws ArgumentException otherwise.</summary>
    public static string NormalizeValue(string key, string? value)
    {
        var definition = Find(key) ?? throw new ArgumentException($"Unknown entitlement '{key}'.");
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (definition.Kind == EntitlementKind.Feature)
        {
            return v is "true" or "false" ? v : throw new ArgumentException($"'{key}' is a feature: use true or false.");
        }
        if (v == Unlimited) return v;
        return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && v.Length <= 9
            ? n.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException($"'{key}' is a limit: use a whole number or 'unlimited'.");
    }
}

/// <summary>
/// The one place that answers "may this clinic do X?" — evaluated from its subscription status and plan, never by
/// comparing plan names. Rules:
///   * billing off (Billing:Enabled = false) -> everything allowed, no limits (today's behaviour)
///   * active, or past_due within the grace period -> the plan's entitlements
///   * no subscription, expired, cancelled, or past_due beyond grace -> data stays viewable, but no sending,
///     campaigns or AI, and every limit is 0
/// Missing keys mean off / 0. A limit of null means unlimited.
/// </summary>
public sealed class ClinicEntitlements
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public ClinicEntitlements(bool billingEnabled, bool hasAccess, string? subscriptionStatus, string? planCode, string? planName,
        DateTimeOffset? periodEnd, IReadOnlyDictionary<string, string> values)
    {
        BillingEnabled = billingEnabled;
        HasAccess = hasAccess;
        SubscriptionStatus = subscriptionStatus;
        PlanCode = planCode;
        PlanName = planName;
        CurrentPeriodEnd = periodEnd;
        _values = values;
    }

    public static ClinicEntitlements Unrestricted { get; } =
        new(false, true, null, null, null, null, new Dictionary<string, string>());

    public bool BillingEnabled { get; }
    /// <summary>The subscription currently grants its plan (active, or past due within grace).</summary>
    public bool HasAccess { get; }
    public string? SubscriptionStatus { get; }
    public string? PlanCode { get; }
    public string? PlanName { get; }
    public DateTimeOffset? CurrentPeriodEnd { get; }

    public bool CanSendMessages => !BillingEnabled || HasAccess;
    public bool CanUseCampaigns => IsEnabled(EntitlementKeys.Campaigns);
    public bool CanUseAiAgent => IsEnabled(EntitlementKeys.AiAgent);
    public bool CanUseApi => IsEnabled(EntitlementKeys.ApiAccess);
    public bool CanUseAdvancedReporting => IsEnabled(EntitlementKeys.AdvancedReporting);
    public int? MaximumAgents => Limit(EntitlementKeys.MaxAgents);
    public int? MaximumWhatsAppNumbers => Limit(EntitlementKeys.MaxWhatsAppNumbers);
    public int? MaximumChannelConnections => Limit(EntitlementKeys.MaxChannelConnections);

    public bool IsEnabled(string featureKey) =>
        !BillingEnabled || (HasAccess && _values.TryGetValue(featureKey, out var v) && v == "true");

    /// <summary>null = unlimited.</summary>
    public int? Limit(string limitKey)
    {
        if (!BillingEnabled) return null;
        if (!HasAccess) return 0;
        if (!_values.TryGetValue(limitKey, out var v)) return 0;
        return v == EntitlementCatalog.Unlimited ? null : int.Parse(v, CultureInfo.InvariantCulture);
    }

    /// <summary>The plan's raw values, for display.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;
}

public interface IEntitlementService
{
    Task<ClinicEntitlements> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Throws EntitlementDeniedException when the clinic has no active plan.</summary>
    Task EnsureCanSendMessagesAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Throws EntitlementDeniedException when the feature isn't in the clinic's active plan.</summary>
    Task EnsureFeatureAsync(Guid clinicId, string featureKey, CancellationToken ct = default);

    /// <summary>Throws EntitlementDeniedException when <paramref name="countAfterChange"/> would exceed the limit.</summary>
    Task EnsureWithinLimitAsync(Guid clinicId, string limitKey, int countAfterChange, CancellationToken ct = default);

    /// <summary>Before connecting (or reconnecting) a messaging channel: checks max_channel_connections, and
    /// max_whatsapp_numbers for WhatsApp. Reconnecting the same channel doesn't count twice.</summary>
    Task EnsureCanConnectChannelAsync(Guid clinicId, string channel, CancellationToken ct = default);
}

public class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _db;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<EntitlementService> _logger;
    private readonly Dictionary<Guid, ClinicEntitlements> _cache = new(); // per request (scoped service)

    public EntitlementService(ApplicationDbContext db, IOptions<BillingOptions> options, TimeProvider time, ILogger<EntitlementService> logger)
    {
        _db = db;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<ClinicEntitlements> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return ClinicEntitlements.Unrestricted;
        if (_cache.TryGetValue(clinicId, out var cached)) return cached;

        var subscription = await _db.ClinicSubscriptions.AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p!.Entitlements)
            .FirstOrDefaultAsync(s => s.ClinicId == clinicId, ct);

        ClinicEntitlements result;
        if (subscription?.Plan is null)
        {
            result = new ClinicEntitlements(true, false, null, null, null, null, new Dictionary<string, string>());
        }
        else
        {
            var now = _time.GetUtcNow();
            var hasAccess = subscription.Status == SubscriptionStatus.Active
                || (subscription.Status == SubscriptionStatus.PastDue
                    && now < (subscription.PastDueSince ?? now).AddDays(Math.Max(0, _options.GracePeriodDays)));
            var values = subscription.Plan.Entitlements.ToDictionary(x => x.EntitlementKey, x => x.Value);
            result = new ClinicEntitlements(true, hasAccess, subscription.Status, subscription.Plan.Code, subscription.Plan.Name,
                subscription.CurrentPeriodEnd, values);
        }
        _cache[clinicId] = result;
        return result;
    }

    public async Task EnsureCanSendMessagesAsync(Guid clinicId, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        if (entitlements.CanSendMessages) return;
        _logger.LogInformation("Billing: clinic {ClinicId} blocked from sending — subscription {Status}.", clinicId, entitlements.SubscriptionStatus ?? "none");
        throw new EntitlementDeniedException("subscription",
            "Your subscription isn't active, so messages can't be sent. See Settings → Billing.");
    }

    public async Task EnsureFeatureAsync(Guid clinicId, string featureKey, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        if (entitlements.IsEnabled(featureKey)) return;
        var label = EntitlementCatalog.Find(featureKey)?.Label ?? featureKey;
        _logger.LogInformation("Billing: clinic {ClinicId} blocked from {Feature} (plan {Plan}, status {Status}).",
            clinicId, featureKey, entitlements.PlanCode ?? "none", entitlements.SubscriptionStatus ?? "none");
        throw new EntitlementDeniedException(featureKey, entitlements.HasAccess
            ? $"{label} isn't included in your plan. See Settings → Billing."
            : $"Your subscription isn't active, so {label.ToLowerInvariant()} can't be used. See Settings → Billing.");
    }

    public async Task EnsureWithinLimitAsync(Guid clinicId, string limitKey, int countAfterChange, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        var limit = entitlements.Limit(limitKey);
        if (limit is null || countAfterChange <= limit) return;
        var label = EntitlementCatalog.Find(limitKey)?.Label ?? limitKey;
        _logger.LogInformation("Billing: clinic {ClinicId} reached its {Limit} limit ({Max}).", clinicId, limitKey, limit);
        throw new EntitlementDeniedException(limitKey, entitlements.HasAccess
            ? $"Your plan allows {limit} {label.ToLowerInvariant()}. See Settings → Billing."
            : "Your subscription isn't active. See Settings → Billing.");
    }

    public async Task EnsureCanConnectChannelAsync(Guid clinicId, string channel, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        if (!entitlements.BillingEnabled) return;

        // One row per (clinic, channel): reconnecting a channel replaces its row, so only the OTHER channels count.
        var otherConnected = await _db.ChannelIntegrations.AsNoTracking()
            .CountAsync(c => c.ClinicId == clinicId && c.Channel != channel && c.Status == ChannelIntegrationStatus.Connected, ct);
        await EnsureWithinLimitAsync(clinicId, EntitlementKeys.MaxChannelConnections, otherConnected + 1, ct);
        if (channel == ChannelType.WhatsApp)
        {
            await EnsureWithinLimitAsync(clinicId, EntitlementKeys.MaxWhatsAppNumbers, 1, ct);
        }
    }
}
