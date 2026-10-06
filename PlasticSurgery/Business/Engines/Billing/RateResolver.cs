using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Business.Engines.Billing;

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
    /// <summary>Loads the candidate rates and picks one. Uses the caller's billing unit of work so it runs inside the
    /// caller's transaction.</summary>
    public static async Task<RateQuote?> ResolveAsync(IBillingRateCardRepository rateCards, RatingRequest request, CancellationToken ct)
    {
        var cards = await rateCards.ListRatingCardsAsync(request.ClinicId, ct);
        if (cards.Count == 0) return null;

        var cardIds = cards.Select(c => c.CardId).ToList();
        var at = request.At;
        var country = NormalizeCountry(request.CountryCode);
        var op = NormalizeDimension(request.Operator);
        var provider = NormalizeDimension(request.Provider);
        var responsibility = request.ProviderBilling;
        // Charging an account where SculptFlow doesn't pay the provider: only a fee set for that arrangement counts.
        var exactResponsibility = request.ForCharging && responsibility != ProviderBillingResponsibility.PlatformFunded;

        var candidates = await rateCards.ListCandidateRatesAsync(cardIds, request.EventType, at, country, op, provider,
            responsibility, exactResponsibility, ct);

        var best = RateSelector.Select(candidates, cards.Select(c => c.CardId).ToList());
        if (best is null) return null;

        var source = cards.First(c => c.CardId == best.RateCardId).Source;
        return new RateQuote(best.Id, best.RateCardId, source, best.ClientRate, best.ProviderCost, best.Currency.Trim(), best.Unit);
    }

    public static string? NormalizeCountry(string? country) =>
        string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();

    /// <summary>Operators and providers are matched case-insensitively by storing them lowercase.</summary>
    public static string? NormalizeDimension(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
