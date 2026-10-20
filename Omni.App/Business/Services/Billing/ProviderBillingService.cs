using Microsoft.Extensions.Options;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Engines.Billing;
using Omni.App.Common.Configs;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Business.Services.Billing;

public class ProviderBillingService : IProviderBillingService
{
    public const string SourceAccount = "account";
    public const string SourceDefault = "default";

    /// <summary>Built-in defaults. WhatsApp stays Omni-funded, which is what billing did before responsibilities
    /// existed (a client's own Meta WABA is usually paid by the client — set that per account, or
    /// Defaults:whatsapp_meta = customer_direct).
    /// Channels without a normal per-message provider fee are no_provider_usage_fee.</summary>
    private static readonly Dictionary<string, string> BuiltInDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["whatsapp"] = ProviderBillingResponsibility.PlatformFunded,
        ["sms"] = ProviderBillingResponsibility.PlatformFunded,
        ["viber"] = ProviderBillingResponsibility.PlatformFunded,
        ["rcs"] = ProviderBillingResponsibility.PlatformFunded,
        ["email"] = ProviderBillingResponsibility.PlatformFunded,
        ["voice"] = ProviderBillingResponsibility.PlatformFunded,
        ["ai"] = ProviderBillingResponsibility.PlatformFunded, // ai_reply: Omni pays the AI provider and charges the client
        ["telegram"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["facebook"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["messenger"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["instagram"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["tiktok"] = ProviderBillingResponsibility.NoProviderUsageFee,
        ["website"] = ProviderBillingResponsibility.NoProviderUsageFee,
    };

    private readonly IBillingUnitOfWorkFactory _units;
    private readonly Dictionary<string, string> _configured;
    private readonly TimeProvider _time;
    private readonly ILogger<ProviderBillingService> _logger;

    public ProviderBillingService(IBillingUnitOfWorkFactory units, IOptions<ProviderBillingOptions> options, TimeProvider time,
        ILogger<ProviderBillingService> logger)
    {
        _units = units;
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

    public async Task<ProviderBillingSetting> ResolveAsync(Guid clientId, string channel, string? provider, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var account = await unit.Channels.GetReadOnlyAsync(clientId, channel, ct);
        var currentProvider = NormalizeProvider(provider) ?? AccountProvider(channel);

        ChannelAccountBillingSettings? settings = null;
        if (account is not null)
        {
            settings = await unit.ChannelBilling.GetReadOnlyAsync(account.Id, ct);
        }
        return Resolve(channel, currentProvider, account?.Id, settings);
    }

    public async Task<IReadOnlyList<ChannelAccountBilling>> ListAccountsAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var accounts = await unit.Channels.ListForClientReadOnlyAsync(clientId, ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var settings = await unit.ChannelBilling.MapReadOnlyAsync(ids, ct);
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

        await using var unit = _units.Create();
        var account = await unit.Channels.GetByIdReadOnlyAsync(channelIntegrationId, ct);
        if (account is null) return null;

        var now = _time.GetUtcNow();
        var settings = await unit.ChannelBilling.GetAsync(channelIntegrationId, ct);
        var before = ToView(account, settings);
        if (settings is null)
        {
            settings = new ChannelAccountBillingSettings { ChannelIntegrationId = account.Id, ClientId = account.ClientId, CreatedAt = now };
            unit.ChannelBilling.Add(settings);
        }
        settings.ProviderBilling = responsibility;
        settings.OmniUsageBilling = change.OmniUsageBilling;
        settings.Reason = change.Reason.Trim();
        settings.UpdatedBy = BillingMath.Truncate(change.Actor, 200);
        settings.UpdatedAt = now;

        var after = ToView(account, settings);
        BillingLedger.LogEvent(unit, account.ClientId, BillingEventTypes.ProviderBillingChanged, BillingSource.Admin, new
        {
            channelIntegrationId = account.Id, channel = account.Channel, provider = after.Provider,
            from = new { before.ProviderBilling, before.OmniUsageBilling, before.Source },
            to = new { after.ProviderBilling, after.OmniUsageBilling, after.Source },
            actor = change.Actor, reason = change.Reason
        }, now);
        await unit.SaveChangesAsync(ct);

        _logger.LogInformation("Billing: provider billing of {Channel} account {AccountId} (client {ClientId}) set to {ProviderBilling}, Omni usage billing {UsageBilling}, by {Actor}: {Reason}.",
            account.Channel, account.Id, account.ClientId, after.ProviderBilling, after.OmniUsageBilling, change.Actor ?? "admin", change.Reason);
        return after;
    }

    public async Task<ChannelAccountBilling?> ResetAsync(Guid channelIntegrationId, string reason, string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required to change who pays the provider.");
        await using var unit = _units.Create();
        var account = await unit.Channels.GetByIdReadOnlyAsync(channelIntegrationId, ct);
        if (account is null) return null;

        var settings = await unit.ChannelBilling.GetAsync(channelIntegrationId, ct);
        var before = ToView(account, settings);
        if (settings is not null)
        {
            unit.ChannelBilling.Remove(settings);
            var after = ToView(account, null);
            BillingLedger.LogEvent(unit, account.ClientId, BillingEventTypes.ProviderBillingChanged, BillingSource.Admin, new
            {
                channelIntegrationId = account.Id, channel = account.Channel, provider = after.Provider,
                from = new { before.ProviderBilling, before.OmniUsageBilling, before.Source },
                to = new { after.ProviderBilling, after.OmniUsageBilling, after.Source },
                actor, reason
            }, _time.GetUtcNow());
            await unit.SaveChangesAsync(ct);
            _logger.LogInformation("Billing: provider billing override of {Channel} account {AccountId} removed by {Actor}: {Reason}.",
                account.Channel, account.Id, actor ?? "admin", reason);
        }
        return ToView(account, null);
    }

    // ------------------------------------------------------------------------------------------------------------

    private ProviderBillingSetting Resolve(string channel, string? currentProvider, Guid? accountId, ChannelAccountBillingSettings? settings)
    {
        var applies = settings is not null;
        var responsibility = applies && settings!.ProviderBilling is not null ? settings.ProviderBilling : DefaultFor(channel, currentProvider);
        var charges = applies && settings!.OmniUsageBilling is bool overridden
            ? overridden
            : responsibility == ProviderBillingResponsibility.PlatformFunded;
        return new ProviderBillingSetting(responsibility, charges, accountId, applies ? SourceAccount : SourceDefault);
    }

    private ChannelAccountBilling ToView(ChannelIntegration account, ChannelAccountBillingSettings? settings)
    {
        var provider = AccountProvider(account.Channel);
        var resolved = Resolve(account.Channel, provider, account.Id, settings);
        return new ChannelAccountBilling(
            account.Id, account.Channel, provider, account.Status, account.DisplayName,
            resolved.Responsibility, ProviderBillingResponsibility.AdminLabel(resolved.Responsibility), resolved.ChargesUsage, resolved.Source,
            DefaultFor(account.Channel, provider), settings?.ProviderBilling, settings?.OmniUsageBilling,
            settings?.Reason, settings?.UpdatedBy, settings?.UpdatedAt);
    }

    /// <summary>The provider an account goes through: Meta for WhatsApp; other channels don't name one.</summary>
    private static string? AccountProvider(string channel) =>
        string.Equals(channel, ChannelType.WhatsApp, StringComparison.OrdinalIgnoreCase) ? ChannelProvider.Meta : null;

    private static string? NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? null : provider.Trim().ToLowerInvariant();
}
