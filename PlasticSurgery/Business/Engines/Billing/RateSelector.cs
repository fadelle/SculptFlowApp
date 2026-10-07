using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Engines.Billing;

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
