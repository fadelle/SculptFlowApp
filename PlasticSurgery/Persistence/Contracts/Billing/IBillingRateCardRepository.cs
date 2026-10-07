using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>billing.rate_cards and their versioned rates. Tracked unless "ReadOnly".</summary>
public interface IBillingRateCardRepository
{
    // ---- cards
    /// <summary>Default card first, then by code.</summary>
    Task<IReadOnlyList<BillingRateCard>> ListReadOnlyAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> MapCodesAsync(CancellationToken ct = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);

    Task<bool> ClinicHasCardAsync(Guid clinicId, CancellationToken ct = default);

    Task<string?> GetClinicCardCodeAsync(Guid clinicId, CancellationToken ct = default);

    Task<BillingRateCard?> GetAsync(string code, CancellationToken ct = default);

    Task<BillingRateCard?> GetReadOnlyAsync(string code, CancellationToken ct = default);

    Task<string> GetCodeAsync(Guid cardId, CancellationToken ct = default);

    Task<IReadOnlyList<BillingRateCard>> ListDefaultCardsAsync(CancellationToken ct = default);

    void Add(BillingRateCard card);

    /// <summary>Locks the card row FOR UPDATE until the transaction ends (serializes version changes of its rates).</summary>
    Task LockAsync(Guid cardId, CancellationToken ct = default);

    // ---- rates
    /// <summary>Rates in effect at <paramref name="now"/>, per card.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountCurrentRatesAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Read-only; without history only rates not yet ended at <paramref name="now"/>.</summary>
    Task<IReadOnlyList<BillingRate>> ListRatesAsync(Guid cardId, bool includeHistory, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Every version of one price point (same event type, country, operator, provider and responsibility).</summary>
    Task<IReadOnlyList<BillingRate>> ListRateVersionsAsync(Guid cardId, string eventType, string? countryCode, string? @operator,
        string? provider, string? providerBilling, CancellationToken ct = default);

    Task<BillingRate?> GetRateAsync(Guid rateId, CancellationToken ct = default);

    void AddRate(BillingRate rate);

    // ---- rating
    /// <summary>The clinic's cards, most specific first (client, plan, default); inactive cards are skipped.</summary>
    Task<IReadOnlyList<RatingCard>> ListRatingCardsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Read-only candidate rates for an event on these cards: effective at <paramref name="at"/>, and each of
    /// country/operator/provider either unset (any) or equal. With <paramref name="exactResponsibility"/> only rates for
    /// exactly that provider-billing responsibility count; otherwise also rates with none set.</summary>
    Task<IReadOnlyList<BillingRate>> ListCandidateRatesAsync(IReadOnlyCollection<Guid> cardIds, string eventType, DateTimeOffset at,
        string? countryCode, string? @operator, string? provider, string? responsibility, bool exactResponsibility,
        CancellationToken ct = default);
}
