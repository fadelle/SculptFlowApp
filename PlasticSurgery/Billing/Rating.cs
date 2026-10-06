using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

/// <summary>The price found for one billable event: per-unit client rate and provider cost, plus where it came from.</summary>
public sealed record RateQuote(Guid RateId, Guid RateCardId, string RateSource, decimal UnitPrice, decimal UnitProviderCost,
    string Currency, string Unit);

/// <summary>What is being priced.</summary>
/// <param name="ProviderBilling">Who pays the upstream provider (ProviderBillingResponsibility).</param>
/// <param name="ForCharging">true = SculptFlow will charge this price. Then a customer-paid / external / no-fee
/// account only matches rates set for its own responsibility (a SculptFlow usage fee) — never a platform rate by
/// fallback. false = the lookup is only for provider-cost reporting, and the generic rate's cost is fine.</param>
public sealed record RatingRequest(Guid ClinicId, string EventType, string? CountryCode, string? Operator, string? Provider,
    DateTimeOffset At, string ProviderBilling = ProviderBillingResponsibility.PlatformFunded, bool ForCharging = true);

/// <summary>
/// Rating: finds the rate for a billable event. Hierarchy (first card with a match wins):
///   1. the clinic's own card (billing.rate_cards.clinic_id = clinic) — custom/enterprise pricing
///   2. the card of the clinic's subscription plan (billing.plans.rate_card_id)
///   3. the default card (is_default)
/// Inside a card the rate must be for the event type and effective at the event's time; a rate's country, operator,
/// provider and provider billing responsibility are either null (any) or must equal the event's — except that
/// charging a non-platform-funded account needs a rate for exactly its responsibility. The most specific match wins
/// (country 8, operator 4, provider 2, responsibility 1). Nothing is ever guessed: no match = no price.
/// </summary>
public static class RateResolver
{
    /// <summary>Loads the candidate rates and picks one. Uses the caller's context so it runs inside the caller's
    /// transaction.</summary>
    public static async Task<RateQuote?> ResolveAsync(ApplicationDbContext db, RatingRequest request, CancellationToken ct)
    {
        var cards = await CardsInOrderAsync(db, request.ClinicId, ct);
        if (cards.Count == 0) return null;

        var cardIds = cards.Select(c => c.CardId).ToList();
        var at = request.At;
        var country = NormalizeCountry(request.CountryCode);
        var op = NormalizeDimension(request.Operator);
        var provider = NormalizeDimension(request.Provider);
        var responsibility = request.ProviderBilling;
        // Charging an account where SculptFlow doesn't pay the provider: only a fee set for that arrangement counts.
        var exactResponsibility = request.ForCharging && responsibility != ProviderBillingResponsibility.PlatformFunded;

        var candidates = await db.BillingRates.AsNoTracking()
            .Where(r => cardIds.Contains(r.RateCardId) && r.EventType == request.EventType
                        && r.EffectiveFrom <= at && (r.EffectiveTo == null || r.EffectiveTo > at)
                        && (r.CountryCode == null || r.CountryCode == country)
                        && (r.Operator == null || r.Operator == op)
                        && (r.Provider == null || r.Provider == provider)
                        && (exactResponsibility
                            ? r.ProviderBilling == responsibility
                            : r.ProviderBilling == null || r.ProviderBilling == responsibility))
            .ToListAsync(ct);

        var best = RateSelector.Select(candidates, cards.Select(c => c.CardId).ToList());
        if (best is null) return null;

        var source = cards.First(c => c.CardId == best.RateCardId).Source;
        return new RateQuote(best.Id, best.RateCardId, source, best.ClientRate, best.ProviderCost, best.Currency.Trim(), best.Unit);
    }

    /// <summary>The clinic's rate cards, most specific first (client, plan, default). Inactive cards are skipped.</summary>
    public static async Task<List<(Guid CardId, string Source)>> CardsInOrderAsync(ApplicationDbContext db, Guid clinicId, CancellationToken ct)
    {
        var result = new List<(Guid, string)>();

        var clientCard = await db.BillingRateCards.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && c.IsActive).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (clientCard.HasValue) result.Add((clientCard.Value, RateSource.Client));

        var planCard = await db.ClinicSubscriptions.AsNoTracking()
            .Where(s => s.ClinicId == clinicId).Select(s => s.Plan!.RateCardId).FirstOrDefaultAsync(ct);
        if (planCard.HasValue && result.All(r => r.Item1 != planCard.Value)
            && await db.BillingRateCards.AnyAsync(c => c.Id == planCard.Value && c.IsActive, ct))
        {
            result.Add((planCard.Value, RateSource.Plan));
        }

        var defaultCard = await db.BillingRateCards.AsNoTracking()
            .Where(c => c.IsDefault && c.IsActive).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (defaultCard.HasValue && result.All(r => r.Item1 != defaultCard.Value)) result.Add((defaultCard.Value, RateSource.Default));

        return result;
    }

    public static string? NormalizeCountry(string? country) =>
        string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();

    /// <summary>Operators and providers are matched case-insensitively by storing them lowercase.</summary>
    public static string? NormalizeDimension(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

/// <summary>The pure selection rule, separate from the database so it can be unit-tested.</summary>
public static class RateSelector
{
    /// <summary>Candidates must already match the event (type, time, null-or-equal dimensions). Lower card rank wins
    /// first (cardOrder index), then higher specificity.</summary>
    public static BillingRate? Select(IEnumerable<BillingRate> candidates, IReadOnlyList<Guid> cardOrder) =>
        candidates
            .Where(r => cardOrder.Contains(r.RateCardId))
            .OrderBy(r => IndexOf(cardOrder, r.RateCardId))
            .ThenByDescending(Specificity)
            .ThenByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

    public static int Specificity(BillingRate rate) =>
        (rate.CountryCode is null ? 0 : 8) + (rate.Operator is null ? 0 : 4) + (rate.Provider is null ? 0 : 2)
        + (rate.ProviderBilling is null ? 0 : 1);

    private static int IndexOf(IReadOnlyList<Guid> list, Guid id)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == id) return i;
        return int.MaxValue;
    }
}
