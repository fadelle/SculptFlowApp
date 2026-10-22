using System.Text.RegularExpressions;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Engines.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Business.Services.Billing;

public partial class AddonService : IAddonService
{
    public const int MaxQuantity = 1000;

    private readonly IBillingUnitOfWorkFactory _units;
    private readonly IConfigManager _config;
    private readonly ICacheManager _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<AddonService> _logger;

    public AddonService(IBillingUnitOfWorkFactory units, IConfigManager config, ICacheManager cache, TimeProvider time, ILogger<AddonService> logger)
    {
        _units = units;
        _config = config;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AddonResponse>> ListCatalogAsync(CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var counts = await unit.Addons.CountClientsByAddonAsync(ct);
        return (await unit.Addons.ListCatalogReadOnlyAsync(ct)).Select(a => ToResponse(a, counts)).ToList();
    }

    public async Task<AddonResponse> SaveAsync(string code, AddonRequest request, CancellationToken ct = default)
    {
        code = (code ?? string.Empty).Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(code)) throw new ArgumentException("Add-on code: 1-50 lowercase letters, digits, '-' or '_'.");
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("Add-on name is required (max 100 characters).");
        if (request.UnitPrice < 0 || decimal.Round(request.UnitPrice, 2) != request.UnitPrice)
            throw new ArgumentException("Unit price must be zero or more, with at most 2 decimals.");

        await using var unit = _units.Create();
        var now = _time.GetUtcNow();
        var addon = await unit.Addons.GetAsync(code, ct);
        var key = (request.EntitlementKey ?? addon?.EntitlementKey ?? string.Empty).Trim().ToLowerInvariant();
        if (addon is null)
        {
            if (EntitlementCatalog.Find(key) is not { Kind: EntitlementKind.Limit })
                throw new ArgumentException($"'{key}' isn't a limit an add-on can raise.");
            if ((await unit.Addons.ListCatalogReadOnlyAsync(ct)).Any(a => a.EntitlementKey == key))
                throw new ArgumentException($"Another add-on already raises '{key}'.");
            addon = new Addon { Id = Guid.NewGuid(), Code = code, EntitlementKey = key, Currency = _config.BillingCurrency, CreatedAt = now };
            unit.Addons.Add(addon);
        }
        else if (key != addon.EntitlementKey)
        {
            throw new ArgumentException("An add-on's entitlement can't change; make a new add-on instead.");
        }
        addon.Name = name;
        addon.UnitPrice = request.UnitPrice;
        addon.IsActive = request.IsActive;
        addon.SortOrder = request.SortOrder;
        addon.UpdatedAt = now;
        await unit.SaveChangesAsync(ct);
        _logger.LogInformation("Billing: add-on {Addon} saved ({Price} {Currency} per unit, active {Active}).", code, addon.UnitPrice, addon.Currency, addon.IsActive);
        return (await ListCatalogAsync(ct)).First(a => a.Code == code);
    }

    public async Task<IReadOnlyList<ClientAddonRow>> ListForClientAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        return ToRows(await unit.Addons.ListForClientReadOnlyAsync(clientId, ct));
    }

    public async Task<IReadOnlyList<ClientAddonRow>> SetQuantityAsync(Guid clientId, string code, int quantity, string idempotencyKey, string source,
        string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > BillingService.MaxKeyLength)
            throw new ArgumentException($"An idempotency key of at most {BillingService.MaxKeyLength} characters is required.");
        if (quantity is < 0 or > MaxQuantity) throw new ArgumentException($"Quantity must be between 0 and {MaxQuantity}.");
        var chargeKey = $"addon:{idempotencyKey}:charge";
        var now = _time.GetUtcNow();

        await using var unit = _units.Create();
        await using var tx = await unit.BeginTransactionAsync(ct);
        var account = await unit.Accounts.LockAsync(clientId, _config.BillingCurrency, ct);
        if (await unit.Ledger.KeyExistsAsync(clientId, chargeKey, ct))
        {
            _logger.LogInformation("Billing: duplicate add-on change {Key} for client {ClientId} ignored.", idempotencyKey, clientId);
            return ToRows(await unit.Addons.ListForClientReadOnlyAsync(clientId, ct));
        }

        var addon = await unit.Addons.GetAsync((code ?? string.Empty).Trim().ToLowerInvariant(), ct)
            ?? throw new KeyNotFoundException("Add-on not found.");
        var row = await unit.Addons.GetForClientAsync(clientId, addon.Id, ct);
        var current = row?.Quantity ?? 0;
        var added = quantity - current;
        if (added == 0) return ToRows(await unit.Addons.ListForClientReadOnlyAsync(clientId, ct));

        if (added > 0)
        {
            if (!addon.IsActive) throw new InvalidOperationException($"The add-on '{addon.Code}' is switched off.");
            var subscription = await unit.Subscriptions.GetWithPlanAsync(clientId, ct);
            if (subscription is null || subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
                throw new InvalidOperationException("Add-ons need an active subscription: start a plan first.");
            if (!string.Equals(addon.Currency.Trim(), account.Currency.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException($"The add-on is priced in {addon.Currency}, but the client's account is in {account.Currency}.");

            var price = added * addon.UnitPrice;
            if (price > 0)
            {
                if (account.WalletBalance < price || account.WalletBalance - price + account.IncludedCreditBalance - account.ReservedAmount < 0)
                {
                    throw new BillingDeniedException(UsageFailureReason.InsufficientFunds,
                        $"The wallet doesn't cover {added} × {addon.Name} ({price:0.00} {account.Currency}). Top up first.");
                }
                await BillingLedger.PostAsync(unit, account, LedgerEntryType.AddonCharge, LedgerBalanceType.Wallet, -price, chargeKey,
                    new LedgerContext(source, actor, $"{addon.Name} × {added}", SubscriptionId: subscription.Id,
                        Reference: addon.Code, CorrelationId: idempotencyKey), now, ct);
            }
        }

        if (quantity == 0)
        {
            unit.Addons.RemoveForClient(row!);
        }
        else if (row is null)
        {
            unit.Addons.AddForClient(new ClientAddon { Id = Guid.NewGuid(), ClientId = clientId, AddonId = addon.Id, Quantity = quantity, CreatedAt = now, UpdatedAt = now });
        }
        else
        {
            row.Quantity = quantity;
            row.UpdatedAt = now;
        }
        BillingLedger.LogEvent(unit, clientId, BillingEventTypes.AddonChanged, source,
            new { addon = addon.Code, from = current, to = quantity, charged = added > 0 ? added * addon.UnitPrice : 0, actor, idempotencyKey }, now);
        await unit.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(clientId), ct);
        _logger.LogInformation("Billing: client {ClientId} add-on {Addon} {From} → {To} by {Actor}.", clientId, addon.Code, current, quantity, actor ?? source);

        await using var read = _units.Create();
        return ToRows(await read.Addons.ListForClientReadOnlyAsync(clientId, ct));
    }

    private static IReadOnlyList<ClientAddonRow> ToRows(IEnumerable<ClientAddon> addons) =>
        addons.Where(a => a.Addon is not null)
            .Select(a => new ClientAddonRow(a.Addon!.Code, a.Addon.Name, a.Addon.EntitlementKey, a.Quantity, a.Addon.UnitPrice, a.Addon.Currency.Trim(),
                a.Quantity * a.Addon.UnitPrice))
            .ToList();

    private static AddonResponse ToResponse(Addon a, IReadOnlyDictionary<Guid, int> counts) =>
        new(a.Code, a.Name, a.EntitlementKey, a.UnitPrice, a.Currency.Trim(), a.IsActive, a.SortOrder, counts.TryGetValue(a.Id, out var n) ? n : 0);

    [GeneratedRegex("^[a-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();
}
