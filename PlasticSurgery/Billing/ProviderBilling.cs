using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

/// <summary>"Billing:ProviderBilling" config: defaults for who pays the upstream provider, overriding the built-in
/// ones. Keys are a channel ("sms") or "{channel}_{provider}" ("whatsapp_meta"); values are a
/// ProviderBillingResponsibility. Example: set Defaults:whatsapp_meta = customer_direct once clinics connect their own
/// WABAs (Embedded Signup) and pay Meta themselves.</summary>
public class ProviderBillingOptions
{
    public Dictionary<string, string>? Defaults { get; set; }
}

/// <summary>The resolved arrangement for one connected channel account.</summary>
/// <param name="Responsibility">Who pays the upstream provider (ProviderBillingResponsibility).</param>
/// <param name="ChargesUsage">Does SculptFlow charge the clinic for usage on this account?</param>
/// <param name="ChannelIntegrationId">The connected account (channel_integrations.id), when the clinic has one.</param>
/// <param name="Source">"account" when an admin override applies, else "default".</param>
public sealed record ProviderBillingSetting(string Responsibility, bool ChargesUsage, Guid? ChannelIntegrationId, string Source);

/// <summary>One connected channel account with its billing arrangement (admin view).</summary>
public sealed record ChannelAccountBilling(
    Guid ChannelIntegrationId, string Channel, string? Provider, string Status, string? DisplayName,
    string ProviderBilling, string ProviderBillingLabel, bool OmniUsageBilling, string Source,
    string DefaultProviderBilling, string? OverrideProviderBilling, bool? OverrideOmniUsageBilling, string? OverrideAppliesToProvider,
    string? OverrideReason, string? OverrideUpdatedBy, DateTimeOffset? OverrideUpdatedAt);

/// <summary>An admin change to one account's arrangement. Null fields keep the default; Reason is required.</summary>
public sealed record ChannelAccountBillingChange(string? ProviderBilling, bool? OmniUsageBilling, string Reason, string? Actor);

/// <summary>
/// Provider billing responsibility — question 2 of the three billing questions (1. what the clinic pays SculptFlow for
/// access = subscription; 2. who pays the upstream provider = this; 3. does SculptFlow charge for the usage). Resolved
/// per CONNECTED CHANNEL ACCOUNT (a channel_integrations row), never assumed per channel:
///   account override (billing.channel_account_settings, only while connected through the same provider)
///   -> Billing:ProviderBilling:Defaults "{channel}_{provider}" -> "{channel}" -> built-in defaults below.
/// SculptFlow usage billing defaults to on only when SculptFlow pays the provider; an override can turn it on for a
/// customer-paid account (a SculptFlow usage fee, priced by rates set for that responsibility) or off for a funded one.
/// </summary>
public interface IProviderBillingService
{
    /// <summary>The arrangement for the clinic's account on this channel (provider = the one the usage goes through).</summary>
    Task<ProviderBillingSetting> ResolveAsync(Guid clinicId, string channel, string? provider, CancellationToken ct = default);

    /// <summary>The default for a channel/provider pair (no account override).</summary>
    string DefaultFor(string channel, string? provider);

    /// <summary>Every connected (or previously connected) channel account of the clinic with its arrangement.</summary>
    Task<IReadOnlyList<ChannelAccountBilling>> ListAccountsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Sets an account's override (applies to future usage only; recorded usage keeps its snapshot).</summary>
    Task<ChannelAccountBilling?> SetAsync(Guid channelIntegrationId, ChannelAccountBillingChange change, CancellationToken ct = default);

    /// <summary>Removes an account's override (back to the defaults).</summary>
    Task<ChannelAccountBilling?> ResetAsync(Guid channelIntegrationId, string reason, string? actor, CancellationToken ct = default);
}

public class ProviderBillingService : IProviderBillingService
{
    public const string SourceAccount = "account";
    public const string SourceDefault = "default";

    /// <summary>Built-in defaults. WhatsApp stays SculptFlow-funded for every provider, which is what billing did before
    /// responsibilities existed (SculptFlow's Infobip account really is funded by SculptFlow; a clinic's own Meta WABA is
    /// usually paid by the clinic — set that per account, or Defaults:whatsapp_meta = customer_direct).
    /// Channels without a normal per-message provider fee are no_provider_usage_fee.</summary>
    private static readonly Dictionary<string, string> BuiltInDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whatsapp"] = ProviderBillingResponsibility.PlatformFunded,
        ["sms"] = ProviderBillingResponsibility.PlatformFunded,
        ["viber"] = ProviderBillingResponsibility.PlatformFunded,
        ["rcs"] = ProviderBillingResponsibility.PlatformFunded,
        ["email"] = ProviderBillingResponsibility.PlatformFunded,
        ["voice"] = ProviderBillingResponsibility.PlatformFunded,
        ["telegram"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["facebook"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["messenger"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["instagram"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["tiktok"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["website"] = ProviderBillingResponsibility.NoProviderUsageFee,
    };

    private readonly BillingDbFactory _dbFactory;
    private readonly Dictionary<string, string> _configured;
    private readonly TimeProvider _time;
    private readonly ILogger<ProviderBillingService> _logger;

    public ProviderBillingService(BillingDbFactory dbFactory, IOptions<ProviderBillingOptions> options, TimeProvider time,
        ILogger<ProviderBillingService> logger)
    {
        _dbFactory = dbFactory;
        _time = time;
        _logger = logger;
        _configured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options.Value.Defaults ?? new Dictionary<string, string>())
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (ProviderBillingResponsibility.IsValid(normalized)) _configured[key.Trim()] = normalized;
            else logger.LogError("Billing: ignoring Billing:ProviderBilling:Defaults:{Key} = '{Value}' (not a provider billing responsibility).", key, value);
        }
    }

    public string DefaultFor(string channel, string? provider)
    {
        channel = (channel ?? string.Empty).Trim().ToLowerInvariant();
        provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim().ToLowerInvariant();
        if (provider is not null && _configured.TryGetValue($"{channel}_{provider}", out var byProvider)) return byProvider;
        if (_configured.TryGetValue(channel, out var byChannel)) return byChannel;
        return BuiltInDefaults.TryGetValue(channel, out var builtIn) ? builtIn : ProviderBillingResponsibility.PlatformFunded;
    }

    public async Task<ProviderBillingSetting> ResolveAsync(Guid clinicId, string channel, string? provider, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var account = await db.ChannelIntegrations.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && c.Channel == channel)
            .Select(c => new { c.Id, c.Channel, c.Provider })
            .FirstOrDefaultAsync(ct);
        var currentProvider = NormalizeProvider(provider) ?? (account is null ? null : AccountProvider(account.Channel, account.Provider));

        ChannelAccountBillingSettings? settings = null;
        if (account is not null)
        {
            settings = await db.ChannelAccountBillingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ChannelIntegrationId == account.Id, ct);
        }
        return Resolve(channel, currentProvider, account?.Id, settings);
    }

    public async Task<IReadOnlyList<ChannelAccountBilling>> ListAccountsAsync(Guid clinicId, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var accounts = await db.ChannelIntegrations.AsNoTracking().Where(c => c.ClinicId == clinicId)
            .OrderBy(c => c.Channel).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var settings = await db.ChannelAccountBillingSettings.AsNoTracking()
            .Where(s => ids.Contains(s.ChannelIntegrationId)).ToDictionaryAsync(s => s.ChannelIntegrationId, ct);
        return accounts.Select(a => ToView(a, settings.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<ChannelAccountBilling?> SetAsync(Guid channelIntegrationId, ChannelAccountBillingChange change, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(change.Reason)) throw new ArgumentException("A reason is required to change who pays the provider.");
        var responsibility = string.IsNullOrWhiteSpace(change.ProviderBilling) ? null : change.ProviderBilling.Trim().ToLowerInvariant();
        if (responsibility is not null && !ProviderBillingResponsibility.IsValid(responsibility))
        {
            throw new ArgumentException($"Provider billing must be one of: {string.Join(", ", ProviderBillingResponsibility.All)}.");
        }
        if (responsibility is null && change.OmniUsageBilling is null)
        {
            throw new ArgumentException("Set providerBilling and/or omniUsageBilling (use reset to go back to the defaults).");
        }

        await using var db = _dbFactory.Create();
        var account = await db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelIntegrationId, ct);
        if (account is null) return null;

        var now = _time.GetUtcNow();
        var settings = await db.ChannelAccountBillingSettings.FirstOrDefaultAsync(s => s.ChannelIntegrationId == channelIntegrationId, ct);
        var before = ToView(account, settings);
        if (settings is null)
        {
            settings = new ChannelAccountBillingSettings { ChannelIntegrationId = account.Id, ClinicId = account.ClinicId, CreatedAt = now };
            db.ChannelAccountBillingSettings.Add(settings);
        }
        settings.ProviderBilling = responsibility;
        settings.OmniUsageBilling = change.OmniUsageBilling;
        // Holds only while the account stays on this provider: a reconnect through another one falls back to defaults.
        settings.AppliesToProvider = AccountProvider(account.Channel, account.Provider);
        settings.Reason = change.Reason.Trim();
        settings.UpdatedBy = BillingStore.Truncate(change.Actor, 200);
        settings.UpdatedAt = now;

        var after = ToView(account, settings);
        BillingStore.LogEvent(db, account.ClinicId, BillingEventTypes.ProviderBillingChanged, BillingSource.Admin, new
        {
            channelIntegrationId = account.Id, channel = account.Channel, provider = after.Provider,
            from = new { before.ProviderBilling, before.OmniUsageBilling, before.Source },
            to = new { after.ProviderBilling, after.OmniUsageBilling, after.Source },
            actor = change.Actor, reason = change.Reason
        }, now);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Billing: provider billing of {Channel} account {AccountId} (clinic {ClinicId}) set to {ProviderBilling}, SculptFlow usage billing {UsageBilling}, by {Actor}: {Reason}.",
            account.Channel, account.Id, account.ClinicId, after.ProviderBilling, after.OmniUsageBilling, change.Actor ?? "admin", change.Reason);
        return after;
    }

    public async Task<ChannelAccountBilling?> ResetAsync(Guid channelIntegrationId, string reason, string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required to change who pays the provider.");
        await using var db = _dbFactory.Create();
        var account = await db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelIntegrationId, ct);
        if (account is null) return null;

        var settings = await db.ChannelAccountBillingSettings.FirstOrDefaultAsync(s => s.ChannelIntegrationId == channelIntegrationId, ct);
        var before = ToView(account, settings);
        if (settings is not null)
        {
            db.ChannelAccountBillingSettings.Remove(settings);
            var after = ToView(account, null);
            BillingStore.LogEvent(db, account.ClinicId, BillingEventTypes.ProviderBillingChanged, BillingSource.Admin, new
            {
                channelIntegrationId = account.Id, channel = account.Channel, provider = after.Provider,
                from = new { before.ProviderBilling, before.OmniUsageBilling, before.Source },
                to = new { after.ProviderBilling, after.OmniUsageBilling, after.Source },
                actor, reason
            }, _time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Billing: provider billing override of {Channel} account {AccountId} removed by {Actor}: {Reason}.",
                account.Channel, account.Id, actor ?? "admin", reason);
        }
        return ToView(account, null);
    }

    // ------------------------------------------------------------------------------------------------------------

    private ProviderBillingSetting Resolve(string channel, string? currentProvider, Guid? accountId, ChannelAccountBillingSettings? settings)
    {
        var applies = settings is not null
                      && (settings.AppliesToProvider is null || string.Equals(settings.AppliesToProvider, currentProvider, StringComparison.OrdinalIgnoreCase));
        var responsibility = applies && settings!.ProviderBilling is not null ? settings.ProviderBilling : DefaultFor(channel, currentProvider);
        var charges = applies && settings!.OmniUsageBilling is bool overridden
            ? overridden
            : responsibility == ProviderBillingResponsibility.PlatformFunded;
        return new ProviderBillingSetting(responsibility, charges, accountId, applies ? SourceAccount : SourceDefault);
    }

    private ChannelAccountBilling ToView(ChannelIntegration account, ChannelAccountBillingSettings? settings)
    {
        var provider = AccountProvider(account.Channel, account.Provider);
        var resolved = Resolve(account.Channel, provider, account.Id, settings);
        return new ChannelAccountBilling(
            account.Id, account.Channel, provider, account.Status, account.DisplayName,
            resolved.Responsibility, ProviderBillingResponsibility.AdminLabel(resolved.Responsibility), resolved.ChargesUsage, resolved.Source,
            DefaultFor(account.Channel, provider), settings?.ProviderBilling, settings?.OmniUsageBilling, settings?.AppliesToProvider,
            settings?.Reason, settings?.UpdatedBy, settings?.UpdatedAt);
    }

    /// <summary>The provider an account goes through: WhatsApp rows record theirs (null = meta); other channels have one.</summary>
    private static string? AccountProvider(string channel, string? provider) =>
        channel == ChannelType.WhatsApp ? (string.IsNullOrWhiteSpace(provider) ? ChannelProvider.Meta : provider.Trim().ToLowerInvariant())
                                        : NormalizeProvider(provider);

    private static string? NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? null : provider.Trim().ToLowerInvariant();
}
