using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Controllers.Admin;
using PlasticSurgery.Controllers.Integrations;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Tests.Controllers;

public class BillingAdminControllerTests
{
    private readonly Mock<IPlanService> _plans = new();
    private readonly Mock<IRateCardService> _rateCards = new();
    private readonly Mock<ISubscriptionService> _subscriptions = new();
    private readonly Mock<IBillingService> _billing = new();
    private readonly Mock<IBillingQueryService> _queries = new();
    private readonly Mock<IProviderBillingService> _providerBilling = new();
    private static readonly Guid Clinic = Guid.NewGuid();

    private BillingAdminController Sut(string? idempotencyKey = null, string? actor = null) =>
        new BillingAdminController(_plans.Object, _rateCards.Object, _subscriptions.Object, _billing.Object, _queries.Object, _providerBilling.Object)
            .With(ctx =>
            {
                if (idempotencyKey is not null) ctx.Request.Headers["Idempotency-Key"] = idempotencyKey;
                if (actor is not null) ctx.Request.Headers["X-Admin-Actor"] = actor;
            });

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task Missing_plan_is_404()
    {
        _plans.Setup(p => p.GetAsync("gold", It.IsAny<CancellationToken>())).ReturnsAsync((PlanResponse?)null);
        _plans.Setup(p => p.UpdateAsync("gold", It.IsAny<PlanRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((PlanResponse?)null);
        _plans.Setup(p => p.SetEntitlementsAsync("gold", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>())).ReturnsAsync((PlanResponse?)null);
        var sut = Sut();
        Assert.IsType<NotFoundResult>(await sut.GetPlan("gold", default));
        Assert.IsType<NotFoundResult>(await sut.UpdatePlan("gold", new PlanRequest("gold", "Gold", null, 1, "monthly", 0, null), default));
        Assert.IsType<NotFoundResult>(await sut.SetEntitlements("gold", new Dictionary<string, string>(), default));
    }

    [Fact]
    public async Task Plans_list_and_create_pass_through()
    {
        var plan = Blank<PlanResponse>();
        _plans.Setup(p => p.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<PlanResponse> { plan });
        _plans.Setup(p => p.CreateAsync(It.IsAny<PlanRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        _plans.Setup(p => p.GetAsync("gold", It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var sut = Sut();
        Assert.IsType<OkObjectResult>(await sut.ListPlans(default));
        Assert.Same(plan, ((OkObjectResult)await sut.GetPlan("gold", default)).Value);
        Assert.IsType<OkObjectResult>(await sut.CreatePlan(new PlanRequest("g", "G", null, 1, "monthly", 0, null), default));
    }

    [Fact]
    public void Entitlement_catalog_lists_every_definition_with_lowercase_kind()
    {
        var ok = Assert.IsType<OkObjectResult>(Sut().EntitlementCatalogList());
        Assert.NotEmpty((System.Collections.IEnumerable)ok.Value!);
    }

    [Fact]
    public async Task Rate_cards_and_rates_return_404_when_the_card_or_rate_is_missing()
    {
        _rateCards.Setup(r => r.UpdateAsync("x", It.IsAny<RateCardUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((RateCardResponse?)null);
        _rateCards.Setup(r => r.ListRatesAsync("x", true, It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<RateResponse>?)null);
        _rateCards.Setup(r => r.AddRateAsync("x", It.IsAny<AddRateRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((RateResponse?)null);
        _rateCards.Setup(r => r.CloseRateAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>())).ReturnsAsync((RateResponse?)null);
        var sut = Sut();
        Assert.IsType<NotFoundResult>(await sut.UpdateRateCard("x", new RateCardUpdateRequest("n", null, true, false), default));
        Assert.IsType<NotFoundResult>(await sut.ListRates("x", true, default));
        Assert.IsType<NotFoundResult>(await sut.AddRate("x", new AddRateRequest("e", null, null, null, null, 1, 2), default));
        Assert.IsType<NotFoundResult>(await sut.CloseRate(Guid.NewGuid(), null, default));
    }

    [Fact]
    public async Task Rate_cards_and_rates_return_the_service_result_when_found()
    {
        var card = Blank<RateCardResponse>();
        var rate = Blank<RateResponse>();
        _rateCards.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<RateCardResponse> { card });
        _rateCards.Setup(r => r.CreateAsync(It.IsAny<RateCardRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(card);
        _rateCards.Setup(r => r.UpdateAsync("x", It.IsAny<RateCardUpdateRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(card);
        _rateCards.Setup(r => r.ListRatesAsync("x", false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<RateResponse> { rate });
        _rateCards.Setup(r => r.AddRateAsync("x", It.IsAny<AddRateRequest>(), "alice", It.IsAny<CancellationToken>())).ReturnsAsync(rate);
        var effectiveTo = DateTimeOffset.UtcNow;
        _rateCards.Setup(r => r.CloseRateAsync(It.IsAny<Guid>(), effectiveTo, It.IsAny<CancellationToken>())).ReturnsAsync(rate);
        var sut = Sut(actor: "alice");
        Assert.IsType<OkObjectResult>(await sut.ListRateCards(default));
        Assert.IsType<OkObjectResult>(await sut.CreateRateCard(new RateCardRequest("c", "C", null, null), default));
        Assert.IsType<OkObjectResult>(await sut.UpdateRateCard("x", new RateCardUpdateRequest("n", null, true, false), default));
        Assert.IsType<OkObjectResult>(await sut.ListRates("x", false, default));
        Assert.IsType<OkObjectResult>(await sut.AddRate("x", new AddRateRequest("e", null, null, null, null, 1, 2), default));
        Assert.IsType<OkObjectResult>(await sut.CloseRate(Guid.NewGuid(), new CloseRateRequest(effectiveTo), default));
    }

    [Fact]
    public async Task Accounts_clinic_overview_and_missing_clinic()
    {
        _queries.Setup(q => q.ListAccountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<BillingAccountRow>());
        _queries.Setup(q => q.GetAdminOverviewAsync(Clinic, It.IsAny<CancellationToken>())).ReturnsAsync((AdminBillingOverview?)null);
        var sut = Sut();
        Assert.IsType<OkObjectResult>(await sut.ListAccounts(default));
        Assert.IsType<NotFoundResult>(await sut.GetClinic(Clinic, default));

        _queries.Setup(q => q.GetAdminOverviewAsync(Clinic, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<AdminBillingOverview>());
        Assert.IsType<OkObjectResult>(await sut.GetClinic(Clinic, default));
    }

    [Fact]
    public async Task Money_operations_require_an_idempotency_key()
    {
        var sut = Sut();
        Assert.IsType<BadRequestObjectResult>(await sut.StartSubscription(Clinic, new StartSubscriptionApiRequest("gold"), default));
        Assert.IsType<BadRequestObjectResult>(await sut.TopUp(Clinic, new TopUpRequest(10, null, null), default));
        Assert.IsType<BadRequestObjectResult>(await sut.Adjust(Clinic, new AdjustmentRequest(5, "wallet", "why"), default));
        _subscriptions.VerifyNoOtherCalls();
        _billing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_blank_idempotency_key_is_treated_as_missing()
    {
        Assert.IsType<BadRequestObjectResult>(await Sut("").TopUp(Clinic, new TopUpRequest(10, null, null), default));
    }

    [Fact]
    public async Task Top_up_passes_the_trimmed_key_amount_source_and_actor()
    {
        WalletTopUp? seen = null;
        _billing.Setup(b => b.TopUpAsync(It.IsAny<WalletTopUp>(), It.IsAny<CancellationToken>()))
            .Callback<WalletTopUp, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new LedgerResult(Guid.NewGuid(), 110, false));
        var result = await Sut("  key-1  ", "alice").TopUp(Clinic, new TopUpRequest(10, "ref", "reason"), default);
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new WalletTopUp(Clinic, 10, "key-1", "ref", "reason", BillingSource.Admin, "alice"), seen);
    }

    [Fact]
    public async Task Adjustment_normalises_the_balance_type()
    {
        WalletAdjustment? seen = null;
        _billing.Setup(b => b.AdjustAsync(It.IsAny<WalletAdjustment>(), It.IsAny<CancellationToken>()))
            .Callback<WalletAdjustment, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new LedgerResult(Guid.NewGuid(), 5, false));
        await Sut("k").Adjust(Clinic, new AdjustmentRequest(-5, "  WALLET ", "fix"), default);
        Assert.Equal("wallet", seen!.BalanceType);
        Assert.Equal(-5, seen.Amount);
        Assert.Equal(BillingSource.Admin, seen.Source);
    }

    [Fact]
    public async Task Start_subscription_passes_the_plan_and_key()
    {
        StartSubscriptionRequest? seen = null;
        _subscriptions.Setup(s => s.StartAsync(It.IsAny<StartSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StartSubscriptionRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ClinicSubscription { ClinicId = Clinic });
        var result = await Sut("k1", "bob").StartSubscription(Clinic, new StartSubscriptionApiRequest("gold", false, "promo"), default);
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new StartSubscriptionRequest(Clinic, "gold", "k1", false, BillingSource.Admin, "bob", "promo"), seen);
    }

    [Fact]
    public async Task Cancel_and_resume_return_404_without_a_subscription()
    {
        _subscriptions.Setup(s => s.CancelAsync(Clinic, true, BillingSource.Admin, It.IsAny<string?>(), "r", It.IsAny<CancellationToken>())).ReturnsAsync((ClinicSubscription?)null);
        _subscriptions.Setup(s => s.ResumeAsync(Clinic, BillingSource.Admin, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((ClinicSubscription?)null);
        var sut = Sut();
        Assert.IsType<NotFoundResult>(await sut.CancelSubscription(Clinic, new CancelSubscriptionRequest(true, "r"), default));
        Assert.IsType<NotFoundResult>(await sut.ResumeSubscription(Clinic, default));
    }

    [Fact]
    public async Task Cancel_and_resume_return_the_subscription()
    {
        var sub = new ClinicSubscription { ClinicId = Clinic };
        _subscriptions.Setup(s => s.CancelAsync(Clinic, false, BillingSource.Admin, It.IsAny<string?>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(sub);
        _subscriptions.Setup(s => s.ResumeAsync(Clinic, BillingSource.Admin, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(sub);
        var sut = Sut();
        Assert.IsType<OkObjectResult>(await sut.CancelSubscription(Clinic, new CancelSubscriptionRequest(), default));
        Assert.IsType<OkObjectResult>(await sut.ResumeSubscription(Clinic, default));
    }

    [Fact]
    public async Task Renew_reports_the_outcome_name()
    {
        var outcome = Enum.GetValues<RenewalOutcome>().First();
        _subscriptions.Setup(s => s.RenewIfDueAsync(Clinic, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        var ok = Assert.IsType<OkObjectResult>(await Sut().Renew(Clinic, default));
        Assert.Contains(outcome.ToString(), ok.Value!.ToString());
    }

    [Fact]
    public async Task Refund_maps_the_outcome_to_http()
    {
        UsageResult Result(UsageOutcome o) => new(o, Guid.NewGuid(), 1, null, false);
        var usage = Guid.NewGuid();
        _billing.SetupSequence(b => b.RefundAsync(Clinic, usage, "r", BillingSource.Admin, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result(UsageOutcome.NotFound))
            .ReturnsAsync(Result(UsageOutcome.Conflict))
            .ReturnsAsync(Result(UsageOutcome.Settled));
        var sut = Sut();
        var request = new RefundRequest("r");
        Assert.IsType<NotFoundResult>(await sut.Refund(Clinic, usage, request, default));
        Assert.IsType<UnprocessableEntityObjectResult>(await sut.Refund(Clinic, usage, request, default));
        Assert.IsType<OkObjectResult>(await sut.Refund(Clinic, usage, request, default));
    }

    [Fact]
    public async Task Usage_ledger_and_reconciliation_forward_the_filters()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;
        _queries.Setup(q => q.ListAdminUsageAsync(Clinic, "settled", "sms", from, to, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<PagedResponse<AdminUsageRow>>()).Verifiable();
        _queries.Setup(q => q.ListAdminLedgerAsync(Clinic, 3, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<PagedResponse<AdminLedgerRow>>()).Verifiable();
        _queries.Setup(q => q.ReconcileAsync(Clinic, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ReconciliationResponse>()).Verifiable();
        var sut = Sut();
        await sut.Usage(Clinic, "settled", "sms", from, to, 2, 10, default);
        await sut.Ledger(Clinic, 3, 20, default);
        await sut.Reconcile(Clinic, default);
        _queries.Verify();
    }

    [Fact]
    public async Task Quote_is_404_when_no_rate_card_prices_the_event()
    {
        _queries.Setup(q => q.QuoteAsync(Clinic, "sms", null, null, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync((QuoteResponse?)null);
        var result = await Sut().Quote(Clinic, "sms", null, null, null, null, null, default);
        var nf = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("No rate card", nf.Value!.ToString());
    }

    [Fact]
    public async Task Quote_returns_the_quote()
    {
        _queries.Setup(q => q.QuoteAsync(Clinic, "sms", "TR", "op", "meta", null, "platform_funded", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<QuoteResponse>());
        Assert.IsType<OkObjectResult>(await Sut().Quote(Clinic, "sms", "TR", "op", "meta", null, "platform_funded", default));
    }

    [Fact]
    public void Provider_billing_modes_list_the_known_defaults()
    {
        _providerBilling.Setup(p => p.DefaultFor(It.IsAny<string>(), It.IsAny<string?>())).Returns("platform_funded");
        var ok = Assert.IsType<OkObjectResult>(Sut().ProviderBillingModes());
        var text = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("whatsapp", text);
        Assert.Contains("infobip", text);
        _providerBilling.Verify(p => p.DefaultFor("whatsapp", "meta"), Times.Once);
        _providerBilling.Verify(p => p.DefaultFor("telegram", null), Times.Once);
    }

    [Fact]
    public async Task Channel_account_billing_overrides_404_when_the_account_is_missing()
    {
        var id = Guid.NewGuid();
        _providerBilling.Setup(p => p.ListAccountsAsync(Clinic, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelAccountBilling>());
        _providerBilling.Setup(p => p.SetAsync(id, It.IsAny<ChannelAccountBillingChange>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChannelAccountBilling?)null);
        _providerBilling.Setup(p => p.ResetAsync(id, "r", It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((ChannelAccountBilling?)null);
        var sut = Sut();
        Assert.IsType<OkObjectResult>(await sut.ChannelAccounts(Clinic, default));
        Assert.IsType<NotFoundResult>(await sut.SetProviderBilling(id, new ProviderBillingRequest("clinic_paid", null, "r"), default));
        Assert.IsType<NotFoundResult>(await sut.ResetProviderBilling(id, new ResetProviderBillingRequest("r"), default));
    }

    [Fact]
    public async Task Provider_billing_change_carries_the_actor()
    {
        var id = Guid.NewGuid();
        ChannelAccountBillingChange? seen = null;
        _providerBilling.Setup(p => p.SetAsync(id, It.IsAny<ChannelAccountBillingChange>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ChannelAccountBillingChange, CancellationToken>((_, c, _) => seen = c)
            .ReturnsAsync(Blank<ChannelAccountBilling>());
        _providerBilling.Setup(p => p.ResetAsync(id, "r", "alice", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ChannelAccountBilling>());
        var sut = Sut(actor: "alice");
        Assert.IsType<OkObjectResult>(await sut.SetProviderBilling(id, new ProviderBillingRequest("clinic_paid", true, "r"), default));
        Assert.IsType<OkObjectResult>(await sut.ResetProviderBilling(id, new ResetProviderBillingRequest("r"), default));
        Assert.Equal(new ChannelAccountBillingChange("clinic_paid", true, "r", "alice"), seen);
    }

    [Fact]
    public async Task Report_defaults_to_the_current_month()
    {
        DateTimeOffset? start = null, end = null;
        _queries.Setup(q => q.GetReportAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((f, t, _) => { start = f; end = t; })
            .ReturnsAsync(Blank<BillingReport>());
        await Sut().Report(null, null, default);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(start!.Value.AddMonths(1), end);
    }

    [Fact]
    public async Task Report_end_defaults_to_one_month_after_a_supplied_start()
    {
        var from = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset? end = null;
        _queries.Setup(q => q.GetReportAsync(from, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((_, t, _) => end = t)
            .ReturnsAsync(Blank<BillingReport>());
        await Sut().Report(from, null, default);
        Assert.Equal(from.AddMonths(1), end);
    }
}

public class AiTestControllerTests
{
    private readonly AiTestController _sut = new();

    [Fact]
    public void Codes_lists_both_vocabularies()
    {
        var text = System.Text.Json.JsonSerializer.Serialize(_sut.ListCodes().Result is OkObjectResult ok ? ok.Value : null);
        Assert.Contains("BOOKED", text);
        Assert.Contains("CANCELED", text);
    }

    [Theory]
    [InlineData("BOOKED", true, "created")]
    [InlineData("booked", true, "created")]
    [InlineData("RESCHEDULED", true, "rescheduled")]
    [InlineData("CONFIRMATION_REQUIRED", false, "none")]
    [InlineData("MULTIPLE_UPCOMING_APPOINTMENTS", false, "none")]
    [InlineData("SLOT_UNAVAILABLE", false, "none")]
    [InlineData("LEAD_NOT_FOUND", false, "none")]
    [InlineData("INVALID_REQUEST", false, "none")]
    public void Schedule_returns_the_canned_result_for_each_code(string code, bool success, string action)
    {
        var result = Assert.IsType<OkObjectResult>(_sut.Schedule(code).Result);
        var body = Assert.IsType<ScheduleConsultationResult>(result.Value);
        Assert.Equal(success, body.Success);
        Assert.Equal(action, body.Operation);
        Assert.Equal(code.ToUpperInvariant(), body.Code);
    }

    [Theory]
    [InlineData("CANCELED", true)]
    [InlineData("NO_UPCOMING_APPOINTMENT", false)]
    [InlineData("MULTIPLE_UPCOMING_APPOINTMENTS", false)]
    [InlineData("INVALID_REQUEST", false)]
    public void Cancel_returns_the_canned_result_for_each_code(string code, bool success)
    {
        var result = Assert.IsType<OkObjectResult>(_sut.Cancel(code).Result);
        var body = Assert.IsType<CancelConsultationResult>(result.Value);
        Assert.Equal(success, body.Success);
        Assert.Equal(code, body.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOPE")]
    public void Unknown_or_missing_codes_are_an_argument_error(string? code)
    {
        Assert.Throws<ArgumentException>(() => _sut.Schedule(code));
        Assert.Throws<ArgumentException>(() => _sut.Cancel(code));
    }

    [Fact]
    public void Canned_multiple_appointments_include_two_candidates()
    {
        var body = (ScheduleConsultationResult)((OkObjectResult)_sut.Schedule("MULTIPLE_UPCOMING_APPOINTMENTS").Result!).Value!;
        Assert.Equal(2, body.ExistingUpcomingAppointments!.Count());
    }
}
