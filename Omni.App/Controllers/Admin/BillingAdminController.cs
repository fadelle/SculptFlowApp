using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Services.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Requests.Billing;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin billing API (internal; Omni admins, never clients): plans and entitlements, rate cards and
/// rate versions, client subscriptions, wallet top-ups/adjustments, refunds, usage and ledger history,
/// reconciliation, price quotes, a revenue report, and per connected channel account who pays the provider. Protected by X-Platform-Admin-Key (RequirePlatformAdminKey); called by the Omni.Admin portal.
/// Money-moving POSTs (top-up, adjustment, start/change plan) require an Idempotency-Key header, so a retried
/// request never applies twice; X-Admin-Actor names the admin in the ledger.
/// Errors: 400 invalid input, 404 unknown id/code, 422 not allowed (e.g. wallet can't pay the plan).
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/billing")]
[ApiErrors]
public class BillingAdminController : ControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";

    private readonly IPlanService _plans;
    private readonly IRateCardService _rateCards;
    private readonly ISubscriptionService _subscriptions;
    private readonly IBillingService _billing;
    private readonly IBillingQueryService _queries;
    private readonly IProviderBillingService _providerBilling;

    public BillingAdminController(IPlanService plans, IRateCardService rateCards, ISubscriptionService subscriptions,
        IBillingService billing, IBillingQueryService queries, IProviderBillingService providerBilling, IAddonService addons)
    {
        _addons = addons;
        _plans = plans;
        _rateCards = rateCards;
        _subscriptions = subscriptions;
        _billing = billing;
        _queries = queries;
        _providerBilling = providerBilling;
    }

    private readonly IAddonService _addons;

    private string Actor => RequirePlatformAdminKeyAttribute.Actor(Request);


    private string? IdempotencyKey => Request.Headers[IdempotencyHeader].FirstOrDefault() is { Length: > 0 } k ? k.Trim() : null;

    // ---- plans ------------------------------------------------------------------------------------------------

    [HttpGet("plans")]
    public async Task<IActionResult> ListPlans(CancellationToken ct) => Ok(await _plans.ListAsync(ct));

    [HttpGet("plans/{code}")]
    public async Task<IActionResult> GetPlan(string code, CancellationToken ct) =>
        await _plans.GetAsync(code, ct) is { } plan ? Ok(plan) : NotFound();

    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] PlanRequest request, CancellationToken ct) =>
        Ok(await _plans.CreateAsync(request, ct));

    [HttpPut("plans/{code}")]
    public async Task<IActionResult> UpdatePlan(string code, [FromBody] PlanRequest request, CancellationToken ct) =>
        await _plans.UpdateAsync(code, request, ct) is { } plan ? Ok(plan) : NotFound();

    [HttpPut("plans/{code}/entitlements")]
    public async Task<IActionResult> SetEntitlements(string code, [FromBody] Dictionary<string, string> entitlements, CancellationToken ct) =>
        await _plans.SetEntitlementsAsync(code, entitlements, ct) is { } plan ? Ok(plan) : NotFound();

    /// <summary>The entitlement keys plans can set, with their kind (feature: true/false, limit: number/unlimited).</summary>
    [HttpGet("entitlements")]
    public IActionResult EntitlementCatalogList() =>
        Ok(EntitlementCatalog.All.Select(d => new { d.Key, Kind = d.Kind.ToString().ToLowerInvariant(), d.Label }));

    // ---- rate cards & rates -----------------------------------------------------------------------------------

    /// <summary>The add-on catalog (extra users, channels, AI bots, WhatsApp numbers) with how many clients have each.</summary>
    [HttpGet("addons")]
    public async Task<IActionResult> ListAddons(CancellationToken ct) => Ok(await _addons.ListCatalogAsync(ct));

    /// <summary>Creates or changes a catalog add-on (name, entitlement on create, unit price per period, active).</summary>
    [HttpPut("addons/{code}")]
    public async Task<IActionResult> SaveAddon(string code, [FromBody] AddonRequest request, CancellationToken ct) =>
        Ok(await _addons.SaveAsync(code, request, ct));

    [HttpGet("clients/{clientId:guid}/addons")]
    public async Task<IActionResult> ClientAddons(Guid clientId, CancellationToken ct) => Ok(await _addons.ListForClientAsync(clientId, ct));

    /// <summary>Sets the client's units of an add-on (0 removes it); added units are charged from the wallet now. Requires
    /// Idempotency-Key. 402-like refusals answer 422 (wallet can't pay, no active subscription).</summary>
    [HttpPut("clients/{clientId:guid}/addons/{code}")]
    public async Task<IActionResult> SetClientAddon(Guid clientId, string code, [FromBody] SetClientAddonRequest request, CancellationToken ct)
    {
        if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
        return Ok(await _addons.SetQuantityAsync(clientId, code, request.Quantity, key, BillingSource.Admin, Actor, ct));
    }

    [HttpGet("rate-cards")]
    public async Task<IActionResult> ListRateCards(CancellationToken ct) => Ok(await _rateCards.ListAsync(ct));

    /// <summary>ClientId set = that client's custom-pricing card (checked before its plan's card and the default).</summary>
    [HttpPost("rate-cards")]
    public async Task<IActionResult> CreateRateCard([FromBody] RateCardRequest request, CancellationToken ct) =>
        Ok(await _rateCards.CreateAsync(request, ct));

    [HttpPut("rate-cards/{code}")]
    public async Task<IActionResult> UpdateRateCard(string code, [FromBody] RateCardUpdateRequest request, CancellationToken ct) =>
        await _rateCards.UpdateAsync(code, request, ct) is { } card ? Ok(card) : NotFound();

    [HttpGet("rate-cards/{code}/rates")]
    public async Task<IActionResult> ListRates(string code, [FromQuery] bool history = false, CancellationToken ct = default) =>
        await _rateCards.ListRatesAsync(code, history, ct) is { } rates ? Ok(rates) : NotFound();

    /// <summary>Adds a rate version (closes the current version of the same rate at its start).</summary>
    [HttpPost("rate-cards/{code}/rates")]
    public async Task<IActionResult> AddRate(string code, [FromBody] AddRateRequest request, CancellationToken ct) =>
        await _rateCards.AddRateAsync(code, request, Actor, ct) is { } rate ? Ok(rate) : NotFound();

    [HttpPost("rates/{id:guid}/close")]
    public async Task<IActionResult> CloseRate(Guid id, [FromBody] CloseRateRequest? request, CancellationToken ct) =>
        await _rateCards.CloseRateAsync(id, request?.EffectiveTo, ct) is { } rate ? Ok(rate) : NotFound();

    // ---- clients: subscription, wallet, history ---------------------------------------------------------------

    [HttpGet("accounts")]
    public async Task<IActionResult> ListAccounts(CancellationToken ct) => Ok(await _queries.ListAccountsAsync(ct));

    [HttpGet("clients/{clientId:guid}")]
    public async Task<IActionResult> GetClient(Guid clientId, CancellationToken ct) =>
        await _queries.GetAdminOverviewAsync(clientId, ct) is { } overview ? Ok(overview) : NotFound();

    /// <summary>Starts a plan or switches to another one (new period from now). Requires Idempotency-Key.</summary>
    [HttpPost("clients/{clientId:guid}/subscription")]
    public async Task<IActionResult> StartSubscription(Guid clientId, [FromBody] StartSubscriptionApiRequest request, CancellationToken ct)
    {
        if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
        var subscription = await _subscriptions.StartAsync(new StartSubscriptionRequest(
            clientId, request.PlanCode, key, request.ChargeFirstPeriod, BillingSource.Admin, Actor, request.Reason), ct);
        return Ok(BillingQueryService.ToSubscription(subscription));
    }

    [HttpPost("clients/{clientId:guid}/subscription/cancel")]
    public async Task<IActionResult> CancelSubscription(Guid clientId, [FromBody] CancelSubscriptionRequest request, CancellationToken ct) =>
        await _subscriptions.CancelAsync(clientId, request.Immediately, BillingSource.Admin, Actor, request.Reason, ct) is { } s
            ? Ok(BillingQueryService.ToSubscription(s)) : NotFound();

    [HttpPost("clients/{clientId:guid}/subscription/resume")]
    public async Task<IActionResult> ResumeSubscription(Guid clientId, CancellationToken ct) =>
        await _subscriptions.ResumeAsync(clientId, BillingSource.Admin, Actor, ct) is { } s
            ? Ok(BillingQueryService.ToSubscription(s)) : NotFound();

    /// <summary>Runs the renewal check now (e.g. right after a top-up for a past-due client) instead of waiting for the worker.</summary>
    [HttpPost("clients/{clientId:guid}/subscription/renew")]
    public async Task<IActionResult> Renew(Guid clientId, CancellationToken ct) =>
        Ok(new { outcome = (await _subscriptions.RenewIfDueAsync(clientId, ct)).ToString() });

    /// <summary>Adds money to the wallet (a payment received outside the app). Requires Idempotency-Key.</summary>
    [HttpPost("clients/{clientId:guid}/wallet/top-ups")]
    public async Task<IActionResult> TopUp(Guid clientId, [FromBody] TopUpRequest request, CancellationToken ct)
    {
        if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
        return Ok(await _billing.TopUpAsync(new WalletTopUp(clientId, request.Amount, key, request.Reference, request.Reason,
            BillingSource.Admin, Actor), ct));
    }

    /// <summary>Manual correction of the wallet or included credit (signed amount, reason required). Requires Idempotency-Key.</summary>
    [HttpPost("clients/{clientId:guid}/wallet/adjustments")]
    public async Task<IActionResult> Adjust(Guid clientId, [FromBody] AdjustmentRequest request, CancellationToken ct)
    {
        if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
        return Ok(await _billing.AdjustAsync(new WalletAdjustment(clientId, request.Amount, (request.BalanceType ?? string.Empty).Trim().ToLowerInvariant(),
            request.Reason, key, BillingSource.Admin, Actor), ct));
    }

    [HttpGet("clients/{clientId:guid}/usage")]
    public async Task<IActionResult> Usage(Guid clientId, [FromQuery] string? status, [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _queries.ListAdminUsageAsync(clientId, status, eventType, from, to, page, pageSize, ct));

    /// <summary>Refunds one settled usage in full, back to where it was paid from. Once only; reason required.</summary>
    [HttpPost("clients/{clientId:guid}/usage/{usageId:guid}/refund")]
    public async Task<IActionResult> Refund(Guid clientId, Guid usageId, [FromBody] RefundRequest request, CancellationToken ct)
    {
        var result = await _billing.RefundAsync(clientId, usageId, request.Reason, BillingSource.Admin, Actor, ct);
        return result.Outcome switch
        {
            UsageOutcome.NotFound => NotFound(),
            UsageOutcome.Conflict => UnprocessableEntity(new { error = "Only a settled usage can be refunded." }),
            _ => Ok(result)
        };
    }

    [HttpGet("clients/{clientId:guid}/ledger")]
    public async Task<IActionResult> Ledger(Guid clientId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await _queries.ListAdminLedgerAsync(clientId, page, pageSize, ct));

    /// <summary>Do the cached balances equal the ledger sums, and the reserved amount the open reservations?</summary>
    [HttpGet("clients/{clientId:guid}/reconciliation")]
    public async Task<IActionResult> Reconcile(Guid clientId, CancellationToken ct) => Ok(await _queries.ReconcileAsync(clientId, ct));

    /// <summary>What Omni would charge this client for a billable event right now (which card and rate would price
    /// it). providerBilling: the account arrangement to price for (default: Omni pays the provider).</summary>
    [HttpGet("clients/{clientId:guid}/quote")]
    public async Task<IActionResult> Quote(Guid clientId, [FromQuery] string eventType, [FromQuery] string? country, [FromQuery] string? @operator,
        [FromQuery] string? provider, [FromQuery] DateTimeOffset? at, [FromQuery] string? providerBilling, CancellationToken ct) =>
        await _queries.QuoteAsync(clientId, eventType, country, @operator, provider, at, providerBilling, ct) is { } quote
            ? Ok(quote)
            : NotFound(new { error = "No rate card prices this event for this client." });

    // ---- provider billing (who pays the upstream provider, per connected channel account) ---------------------

    /// <summary>The four arrangements with admin-facing labels, and the defaults per channel.</summary>
    [HttpGet("provider-billing")]
    public IActionResult ProviderBillingModes() => Ok(new
    {
        modes = ProviderBillingResponsibility.All.Select(m => new
        {
            value = m,
            label = ProviderBillingResponsibility.AdminLabel(m),
            omniUsageBillingByDefault = m == ProviderBillingResponsibility.PlatformFunded
        }),
        defaults = new[] { "whatsapp:meta", "telegram", "facebook", "instagram", "sms", "viber", "email" }
            .Select(k => k.Split(':'))
            .Select(p => new { channel = p[0], provider = p.Length > 1 ? p[1] : null, providerBilling = _providerBilling.DefaultFor(p[0], p.Length > 1 ? p[1] : null) })
    });

    /// <summary>The client's connected channel accounts with who pays the provider and whether Omni charges usage.</summary>
    [HttpGet("clients/{clientId:guid}/channel-accounts")]
    public async Task<IActionResult> ChannelAccounts(Guid clientId, CancellationToken ct) =>
        Ok(await _providerBilling.ListAccountsAsync(clientId, ct));

    /// <summary>Sets one account's arrangement (applies to future usage only). Body: providerBilling (customer_direct |
    /// platform_funded | external_provider_direct | no_provider_usage_fee, or null = default), omniUsageBilling
    /// (true/false, or null = default: on only when Omni pays the provider), reason (required).</summary>
    [HttpPut("channel-accounts/{channelIntegrationId:guid}/provider-billing")]
    public async Task<IActionResult> SetProviderBilling(Guid channelIntegrationId, [FromBody] ProviderBillingRequest request, CancellationToken ct) =>
        await _providerBilling.SetAsync(channelIntegrationId,
            new ChannelAccountBillingChange(request.ProviderBilling, request.OmniUsageBilling, request.Reason, Actor), ct) is { } account
            ? Ok(account) : NotFound();

    /// <summary>Removes one account's override: back to the channel/provider defaults.</summary>
    [HttpPost("channel-accounts/{channelIntegrationId:guid}/provider-billing/reset")]
    public async Task<IActionResult> ResetProviderBilling(Guid channelIntegrationId, [FromBody] ResetProviderBillingRequest request, CancellationToken ct) =>
        await _providerBilling.ResetAsync(channelIntegrationId, request.Reason, Actor, ct) is { } account
            ? Ok(account) : NotFound();

    /// <summary>Usage revenue, provider cost and margin by client/channel/event type, plus subscription revenue and
    /// top-ups, for [from, to). Defaults to the current calendar month (UTC).</summary>
    [HttpGet("report")]
    public async Task<IActionResult> Report([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var start = from ?? new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return Ok(await _queries.GetReportAsync(start, to ?? start.AddMonths(1), ct));
    }

    // ------------------------------------------------------------------------------------------------------------

    private IActionResult MissingIdempotencyKey() =>
        BadRequest(new { error = $"An {IdempotencyHeader} header is required (any unique string per intended operation, max {BillingService.MaxKeyLength} chars)." });
}
