using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Business.Services.Billing;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Requests.Billing;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin billing API (internal; SculptFlow staff, never clinics): plans and entitlements, rate cards and
/// rate versions, clinic subscriptions, wallet top-ups/adjustments, refunds, usage and ledger history,
/// reconciliation, price quotes, a revenue report, and per connected channel account who pays the provider. Protected by X-Platform-Admin-Key (RequirePlatformAdminKey); called by the SculptFlowAdmin portal.
/// Money-moving POSTs (top-up, adjustment, start/change plan) require an Idempotency-Key header, so a retried
/// request never applies twice; X-Admin-Actor names the admin in the ledger.
/// Errors: 400 invalid input, 404 unknown id/code, 422 not allowed (e.g. wallet can't pay the plan).
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/billing")]
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
        IBillingService billing, IBillingQueryService queries, IProviderBillingService providerBilling)
    {
        _plans = plans;
        _rateCards = rateCards;
        _subscriptions = subscriptions;
        _billing = billing;
        _queries = queries;
        _providerBilling = providerBilling;
    }

    private string Actor => RequirePlatformAdminKeyAttribute.Actor(Request);


    private string? IdempotencyKey => Request.Headers[IdempotencyHeader].FirstOrDefault() is { Length: > 0 } k ? k.Trim() : null;

    // ---- plans ------------------------------------------------------------------------------------------------

    [HttpGet("plans")]
    public async Task<IActionResult> ListPlans(CancellationToken ct) => Ok(await _plans.ListAsync(ct));

    [HttpGet("plans/{code}")]
    public async Task<IActionResult> GetPlan(string code, CancellationToken ct) =>
        await _plans.GetAsync(code, ct) is { } plan ? Ok(plan) : NotFound();

    [HttpPost("plans")]
    public Task<IActionResult> CreatePlan([FromBody] PlanRequest request, CancellationToken ct) =>
        Run(async () => (IActionResult)Ok(await _plans.CreateAsync(request, ct)));

    [HttpPut("plans/{code}")]
    public Task<IActionResult> UpdatePlan(string code, [FromBody] PlanRequest request, CancellationToken ct) =>
        Run(async () => await _plans.UpdateAsync(code, request, ct) is { } plan ? Ok(plan) : NotFound());

    [HttpPut("plans/{code}/entitlements")]
    public Task<IActionResult> SetEntitlements(string code, [FromBody] Dictionary<string, string> entitlements, CancellationToken ct) =>
        Run(async () => await _plans.SetEntitlementsAsync(code, entitlements, ct) is { } plan ? Ok(plan) : NotFound());

    /// <summary>The entitlement keys plans can set, with their kind (feature: true/false, limit: number/unlimited).</summary>
    [HttpGet("entitlements")]
    public IActionResult EntitlementCatalogList() =>
        Ok(EntitlementCatalog.All.Select(d => new { d.Key, Kind = d.Kind.ToString().ToLowerInvariant(), d.Label }));

    // ---- rate cards & rates -----------------------------------------------------------------------------------

    [HttpGet("rate-cards")]
    public async Task<IActionResult> ListRateCards(CancellationToken ct) => Ok(await _rateCards.ListAsync(ct));

    /// <summary>ClinicId set = that clinic's custom-pricing card (checked before its plan's card and the default).</summary>
    [HttpPost("rate-cards")]
    public Task<IActionResult> CreateRateCard([FromBody] RateCardRequest request, CancellationToken ct) =>
        Run(async () => (IActionResult)Ok(await _rateCards.CreateAsync(request, ct)));

    [HttpPut("rate-cards/{code}")]
    public Task<IActionResult> UpdateRateCard(string code, [FromBody] RateCardUpdateRequest request, CancellationToken ct) =>
        Run(async () => await _rateCards.UpdateAsync(code, request, ct) is { } card ? Ok(card) : NotFound());

    [HttpGet("rate-cards/{code}/rates")]
    public async Task<IActionResult> ListRates(string code, [FromQuery] bool history = false, CancellationToken ct = default) =>
        await _rateCards.ListRatesAsync(code, history, ct) is { } rates ? Ok(rates) : NotFound();

    /// <summary>Adds a rate version (closes the current version of the same rate at its start).</summary>
    [HttpPost("rate-cards/{code}/rates")]
    public Task<IActionResult> AddRate(string code, [FromBody] AddRateRequest request, CancellationToken ct) =>
        Run(async () => await _rateCards.AddRateAsync(code, request, Actor, ct) is { } rate ? Ok(rate) : NotFound());

    [HttpPost("rates/{id:guid}/close")]
    public Task<IActionResult> CloseRate(Guid id, [FromBody] CloseRateRequest? request, CancellationToken ct) =>
        Run(async () => await _rateCards.CloseRateAsync(id, request?.EffectiveTo, ct) is { } rate ? Ok(rate) : NotFound());

    // ---- clinics: subscription, wallet, history ---------------------------------------------------------------

    [HttpGet("accounts")]
    public async Task<IActionResult> ListAccounts(CancellationToken ct) => Ok(await _queries.ListAccountsAsync(ct));

    [HttpGet("clinics/{clinicId:guid}")]
    public async Task<IActionResult> GetClinic(Guid clinicId, CancellationToken ct) =>
        await _queries.GetAdminOverviewAsync(clinicId, ct) is { } overview ? Ok(overview) : NotFound();

    /// <summary>Starts a plan or switches to another one (new period from now). Requires Idempotency-Key.</summary>
    [HttpPost("clinics/{clinicId:guid}/subscription")]
    public Task<IActionResult> StartSubscription(Guid clinicId, [FromBody] StartSubscriptionApiRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
            var subscription = await _subscriptions.StartAsync(new StartSubscriptionRequest(
                clinicId, request.PlanCode, key, request.ChargeFirstPeriod, BillingSource.Admin, Actor, request.Reason), ct);
            return Ok(BillingQueryService.ToSubscription(subscription));
        });

    [HttpPost("clinics/{clinicId:guid}/subscription/cancel")]
    public Task<IActionResult> CancelSubscription(Guid clinicId, [FromBody] CancelSubscriptionRequest request, CancellationToken ct) =>
        Run(async () => await _subscriptions.CancelAsync(clinicId, request.Immediately, BillingSource.Admin, Actor, request.Reason, ct) is { } s
            ? Ok(BillingQueryService.ToSubscription(s)) : NotFound());

    [HttpPost("clinics/{clinicId:guid}/subscription/resume")]
    public Task<IActionResult> ResumeSubscription(Guid clinicId, CancellationToken ct) =>
        Run(async () => await _subscriptions.ResumeAsync(clinicId, BillingSource.Admin, Actor, ct) is { } s
            ? Ok(BillingQueryService.ToSubscription(s)) : NotFound());

    /// <summary>Runs the renewal check now (e.g. right after a top-up for a past-due clinic) instead of waiting for the worker.</summary>
    [HttpPost("clinics/{clinicId:guid}/subscription/renew")]
    public Task<IActionResult> Renew(Guid clinicId, CancellationToken ct) =>
        Run(async () => (IActionResult)Ok(new { outcome = (await _subscriptions.RenewIfDueAsync(clinicId, ct)).ToString() }));

    /// <summary>Adds money to the wallet (a payment received outside the app). Requires Idempotency-Key.</summary>
    [HttpPost("clinics/{clinicId:guid}/wallet/top-ups")]
    public Task<IActionResult> TopUp(Guid clinicId, [FromBody] TopUpRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
            return Ok(await _billing.TopUpAsync(new WalletTopUp(clinicId, request.Amount, key, request.Reference, request.Reason,
                BillingSource.Admin, Actor), ct));
        });

    /// <summary>Manual correction of the wallet or included credit (signed amount, reason required). Requires Idempotency-Key.</summary>
    [HttpPost("clinics/{clinicId:guid}/wallet/adjustments")]
    public Task<IActionResult> Adjust(Guid clinicId, [FromBody] AdjustmentRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            if (IdempotencyKey is not { } key) return MissingIdempotencyKey();
            return Ok(await _billing.AdjustAsync(new WalletAdjustment(clinicId, request.Amount, (request.BalanceType ?? string.Empty).Trim().ToLowerInvariant(),
                request.Reason, key, BillingSource.Admin, Actor), ct));
        });

    [HttpGet("clinics/{clinicId:guid}/usage")]
    public async Task<IActionResult> Usage(Guid clinicId, [FromQuery] string? status, [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await _queries.ListAdminUsageAsync(clinicId, status, eventType, from, to, page, pageSize, ct));

    /// <summary>Refunds one settled usage in full, back to where it was paid from. Once only; reason required.</summary>
    [HttpPost("clinics/{clinicId:guid}/usage/{usageId:guid}/refund")]
    public Task<IActionResult> Refund(Guid clinicId, Guid usageId, [FromBody] RefundRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            var result = await _billing.RefundAsync(clinicId, usageId, request.Reason, BillingSource.Admin, Actor, ct);
            return result.Outcome switch
            {
                UsageOutcome.NotFound => NotFound(),
                UsageOutcome.Conflict => UnprocessableEntity(new { error = "Only a settled usage can be refunded." }),
                _ => Ok(result)
            };
        });

    [HttpGet("clinics/{clinicId:guid}/ledger")]
    public async Task<IActionResult> Ledger(Guid clinicId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await _queries.ListAdminLedgerAsync(clinicId, page, pageSize, ct));

    /// <summary>Do the cached balances equal the ledger sums, and the reserved amount the open reservations?</summary>
    [HttpGet("clinics/{clinicId:guid}/reconciliation")]
    public async Task<IActionResult> Reconcile(Guid clinicId, CancellationToken ct) => Ok(await _queries.ReconcileAsync(clinicId, ct));

    /// <summary>What SculptFlow would charge this clinic for a billable event right now (which card and rate would price
    /// it). providerBilling: the account arrangement to price for (default: SculptFlow pays the provider).</summary>
    [HttpGet("clinics/{clinicId:guid}/quote")]
    public async Task<IActionResult> Quote(Guid clinicId, [FromQuery] string eventType, [FromQuery] string? country, [FromQuery] string? @operator,
        [FromQuery] string? provider, [FromQuery] DateTimeOffset? at, [FromQuery] string? providerBilling, CancellationToken ct) =>
        await _queries.QuoteAsync(clinicId, eventType, country, @operator, provider, at, providerBilling, ct) is { } quote
            ? Ok(quote)
            : NotFound(new { error = "No rate card prices this event for this clinic." });

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
        defaults = new[] { "whatsapp:meta", "whatsapp:infobip", "telegram", "facebook", "instagram", "sms", "viber", "email" }
            .Select(k => k.Split(':'))
            .Select(p => new { channel = p[0], provider = p.Length > 1 ? p[1] : null, providerBilling = _providerBilling.DefaultFor(p[0], p.Length > 1 ? p[1] : null) })
    });

    /// <summary>The clinic's connected channel accounts with who pays the provider and whether SculptFlow charges usage.</summary>
    [HttpGet("clinics/{clinicId:guid}/channel-accounts")]
    public async Task<IActionResult> ChannelAccounts(Guid clinicId, CancellationToken ct) =>
        Ok(await _providerBilling.ListAccountsAsync(clinicId, ct));

    /// <summary>Sets one account's arrangement (applies to future usage only). Body: providerBilling (customer_direct |
    /// platform_funded | external_provider_direct | no_provider_usage_fee, or null = default), omniUsageBilling
    /// (true/false, or null = default: on only when SculptFlow pays the provider), reason (required).</summary>
    [HttpPut("channel-accounts/{channelIntegrationId:guid}/provider-billing")]
    public Task<IActionResult> SetProviderBilling(Guid channelIntegrationId, [FromBody] ProviderBillingRequest request, CancellationToken ct) =>
        Run(async () => await _providerBilling.SetAsync(channelIntegrationId,
            new ChannelAccountBillingChange(request.ProviderBilling, request.OmniUsageBilling, request.Reason, Actor), ct) is { } account
            ? Ok(account) : NotFound());

    /// <summary>Removes one account's override: back to the channel/provider defaults.</summary>
    [HttpPost("channel-accounts/{channelIntegrationId:guid}/provider-billing/reset")]
    public Task<IActionResult> ResetProviderBilling(Guid channelIntegrationId, [FromBody] ResetProviderBillingRequest request, CancellationToken ct) =>
        Run(async () => await _providerBilling.ResetAsync(channelIntegrationId, request.Reason, Actor, ct) is { } account
            ? Ok(account) : NotFound());

    /// <summary>Usage revenue, provider cost and margin by clinic/channel/event type, plus subscription revenue and
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

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }
}
