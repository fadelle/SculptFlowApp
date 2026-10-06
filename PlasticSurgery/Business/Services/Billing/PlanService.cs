using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Common.Configs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Business.Services.Billing;

public partial class PlanService : IPlanService
{
    private readonly IBillingUnitOfWorkFactory _units;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PlanService> _logger;

    public PlanService(IBillingUnitOfWorkFactory units, IOptions<BillingOptions> options, TimeProvider time, ILogger<PlanService> logger)
    {
        _units = units;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlanResponse>> ListAsync(CancellationToken ct = default)
    {
        await using var unit = _units.Create();
        var plans = await unit.Plans.ListWithEntitlementsReadOnlyAsync(ct);
        var cardCodes = await unit.RateCards.MapCodesAsync(ct);
        var subscribers = await unit.Subscriptions.CountSubscribersByPlanAsync(ct);
        return plans.Select(p => ToResponse(p, cardCodes, subscribers)).ToList();
    }

    public async Task<PlanResponse?> GetAsync(string code, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(p => p.Code == Normalize(code));

    public async Task<PlanResponse> CreateAsync(PlanRequest request, CancellationToken ct = default)
    {
        var code = Normalize(request.Code);
        if (!CodePattern().IsMatch(code)) throw new ArgumentException("Plan code: 1-50 lowercase letters, digits, '-' or '_'.");

        await using var unit = _units.Create();
        if (await unit.Plans.CodeExistsAsync(code, ct)) throw new ArgumentException($"A plan with code '{code}' already exists.");

        var now = _time.GetUtcNow();
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Code = code, Currency = _options.NormalizedCurrency, CreatedAt = now };
        await ApplyAsync(unit, plan, request, now, ct);
        unit.Plans.Add(plan);
        ReplaceEntitlements(unit, plan, request.Entitlements ?? new Dictionary<string, string>(), now);
        await unit.SaveChangesAsync(ct);

        _logger.LogInformation("Billing: plan {Plan} created ({Price} {Currency} per {Period}, credit {Credit}).",
            code, plan.Price, plan.Currency, plan.BillingPeriod, plan.IncludedUsageCredit);
        return (await GetAsync(code, ct))!;
    }

    public async Task<PlanResponse?> UpdateAsync(string code, PlanRequest request, CancellationToken ct = default)
    {
        code = Normalize(code);
        await using var unit = _units.Create();
        var plan = await unit.Plans.GetWithEntitlementsAsync(code, ct);
        if (plan is null) return null;

        var now = _time.GetUtcNow();
        await ApplyAsync(unit, plan, request, now, ct);
        if (request.Entitlements is not null) ReplaceEntitlements(unit, plan, request.Entitlements, now);
        await unit.SaveChangesAsync(ct);

        _logger.LogInformation("Billing: plan {Plan} updated ({Price} {Currency} per {Period}, credit {Credit}, active {Active}); applies from each subscriber's next renewal.",
            code, plan.Price, plan.Currency, plan.BillingPeriod, plan.IncludedUsageCredit, plan.IsActive);
        return await GetAsync(code, ct);
    }

    public async Task<PlanResponse?> SetEntitlementsAsync(string code, IReadOnlyDictionary<string, string> entitlements, CancellationToken ct = default)
    {
        code = Normalize(code);
        await using var unit = _units.Create();
        var plan = await unit.Plans.GetWithEntitlementsAsync(code, ct);
        if (plan is null) return null;
        ReplaceEntitlements(unit, plan, entitlements, _time.GetUtcNow());
        await unit.SaveChangesAsync(ct);
        _logger.LogInformation("Billing: entitlements of plan {Plan} replaced: {Entitlements}.", code,
            string.Join(", ", entitlements.Select(e => $"{e.Key}={e.Value}")));
        return await GetAsync(code, ct);
    }

    private async Task ApplyAsync(IBillingUnitOfWork unit, SubscriptionPlan plan, PlanRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("Plan name is required (max 100 characters).");
        if (request.Price < 0 || decimal.Round(request.Price, 2) != request.Price) throw new ArgumentException("Price must be zero or more, with at most 2 decimals.");
        if (request.IncludedUsageCredit < 0 || decimal.Round(request.IncludedUsageCredit, 2) != request.IncludedUsageCredit)
        {
            throw new ArgumentException("Included usage credit must be zero or more, with at most 2 decimals.");
        }
        var period = (request.BillingPeriod ?? string.Empty).Trim().ToLowerInvariant();
        if (!BillingPeriod.IsValid(period)) throw new ArgumentException("Billing period must be 'month' or 'year'.");

        Guid? rateCardId = null;
        if (!string.IsNullOrWhiteSpace(request.RateCardCode))
        {
            var cardCode = request.RateCardCode.Trim().ToLowerInvariant();
            var card = await unit.RateCards.GetAsync(cardCode, ct)
                ?? throw new ArgumentException($"Rate card '{cardCode}' doesn't exist.");
            if (card.ClinicId is not null) throw new ArgumentException("A clinic's own rate card can't be a plan's rate card.");
            rateCardId = card.Id;
        }

        plan.Name = name;
        plan.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        plan.Price = request.Price;
        plan.BillingPeriod = period;
        plan.IncludedUsageCredit = request.IncludedUsageCredit;
        plan.RateCardId = rateCardId;
        plan.IsActive = request.IsActive;
        plan.SortOrder = request.SortOrder;
        plan.UpdatedAt = now;
    }

    private static void ReplaceEntitlements(IBillingUnitOfWork unit, SubscriptionPlan plan, IReadOnlyDictionary<string, string> values, DateTimeOffset now)
    {
        var normalized = values.ToDictionary(
            kv => (kv.Key ?? string.Empty).Trim().ToLowerInvariant(),
            kv => EntitlementCatalog.NormalizeValue((kv.Key ?? string.Empty).Trim().ToLowerInvariant(), kv.Value));

        foreach (var existing in plan.Entitlements.ToList())
        {
            if (!normalized.ContainsKey(existing.EntitlementKey))
            {
                unit.Plans.RemoveEntitlement(existing);
                plan.Entitlements.Remove(existing);
            }
        }
        foreach (var (key, value) in normalized)
        {
            var row = plan.Entitlements.FirstOrDefault(e => e.EntitlementKey == key);
            if (row is null)
            {
                row = new SubscriptionPlanEntitlement
                {
                    Id = Guid.NewGuid(), PlanId = plan.Id, EntitlementKey = key, Value = value, CreatedAt = now, UpdatedAt = now
                };
                unit.Plans.AddEntitlement(row); // explicit Add: a set Guid key found via navigation would be taken for an existing row
                plan.Entitlements.Add(row);
            }
            else if (row.Value != value)
            {
                row.Value = value;
                row.UpdatedAt = now;
            }
        }
    }

    private static PlanResponse ToResponse(SubscriptionPlan p, IReadOnlyDictionary<Guid, string> cardCodes, IReadOnlyDictionary<Guid, int> subscribers) =>
        new(p.Id, p.Code, p.Name, p.Description, p.Price, p.Currency.Trim(), p.BillingPeriod, p.IncludedUsageCredit,
            p.RateCardId is Guid id && cardCodes.TryGetValue(id, out var code) ? code : null,
            p.IsActive, p.SortOrder,
            p.Entitlements.OrderBy(e => e.EntitlementKey).ToDictionary(e => e.EntitlementKey, e => e.Value),
            subscribers.TryGetValue(p.Id, out var n) ? n : 0);

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();
}
