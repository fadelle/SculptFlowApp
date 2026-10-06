using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public class BillingRateCardRepository : IBillingRateCardRepository
{
    private readonly ApplicationDbContext _db;

    public BillingRateCardRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BillingRateCard>> ListReadOnlyAsync(CancellationToken ct = default) =>
        await _db.BillingRateCards.AsNoTracking().OrderByDescending(c => c.IsDefault).ThenBy(c => c.Code).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> MapCodesAsync(CancellationToken ct = default) =>
        await _db.BillingRateCards.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        _db.BillingRateCards.AnyAsync(c => c.Code == code, ct);

    public Task<bool> ClinicHasCardAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.BillingRateCards.AnyAsync(c => c.ClinicId == clinicId, ct);

    public Task<string?> GetClinicCardCodeAsync(Guid clinicId, CancellationToken ct = default) =>
        _db.BillingRateCards.AsNoTracking().Where(c => c.ClinicId == clinicId).Select(c => c.Code).FirstOrDefaultAsync(ct);

    public Task<BillingRateCard?> GetAsync(string code, CancellationToken ct = default) =>
        _db.BillingRateCards.FirstOrDefaultAsync(c => c.Code == code, ct);

    public Task<BillingRateCard?> GetReadOnlyAsync(string code, CancellationToken ct = default) =>
        _db.BillingRateCards.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, ct);

    public Task<string> GetCodeAsync(Guid cardId, CancellationToken ct = default) =>
        _db.BillingRateCards.Where(c => c.Id == cardId).Select(c => c.Code).FirstAsync(ct);

    public async Task<IReadOnlyList<BillingRateCard>> ListDefaultCardsAsync(CancellationToken ct = default) =>
        await _db.BillingRateCards.Where(c => c.IsDefault).ToListAsync(ct);

    public void Add(BillingRateCard card) => _db.BillingRateCards.Add(card);

    public Task LockAsync(Guid cardId, CancellationToken ct = default) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"select 1 from billing.rate_cards where id = {cardId} for update", ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountCurrentRatesAsync(DateTimeOffset now, CancellationToken ct = default) =>
        await _db.BillingRates.AsNoTracking()
            .Where(r => r.EffectiveFrom <= now && (r.EffectiveTo == null || r.EffectiveTo > now))
            .GroupBy(r => r.RateCardId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<IReadOnlyList<BillingRate>> ListRatesAsync(Guid cardId, bool includeHistory, DateTimeOffset now,
        CancellationToken ct = default)
    {
        var query = _db.BillingRates.AsNoTracking().Where(r => r.RateCardId == cardId);
        if (!includeHistory) query = query.Where(r => r.EffectiveTo == null || r.EffectiveTo > now);
        return await query.OrderBy(r => r.EventType).ThenBy(r => r.CountryCode).ThenBy(r => r.Provider).ThenBy(r => r.EffectiveFrom)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BillingRate>> ListRateVersionsAsync(Guid cardId, string eventType, string? countryCode,
        string? @operator, string? provider, string? providerBilling, CancellationToken ct = default) =>
        await _db.BillingRates
            .Where(r => r.RateCardId == cardId && r.EventType == eventType && r.CountryCode == countryCode && r.Operator == @operator
                        && r.Provider == provider && r.ProviderBilling == providerBilling)
            .ToListAsync(ct);

    public Task<BillingRate?> GetRateAsync(Guid rateId, CancellationToken ct = default) =>
        _db.BillingRates.FirstOrDefaultAsync(r => r.Id == rateId, ct);

    public void AddRate(BillingRate rate) => _db.BillingRates.Add(rate);

    public async Task<IReadOnlyList<RatingCard>> ListRatingCardsAsync(Guid clinicId, CancellationToken ct = default)
    {
        var result = new List<RatingCard>();

        var clientCard = await _db.BillingRateCards.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && c.IsActive).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (clientCard.HasValue) result.Add(new RatingCard(clientCard.Value, RateSource.Client));

        var planCard = await _db.ClinicSubscriptions.AsNoTracking()
            .Where(s => s.ClinicId == clinicId).Select(s => s.Plan!.RateCardId).FirstOrDefaultAsync(ct);
        if (planCard.HasValue && result.All(r => r.CardId != planCard.Value)
            && await _db.BillingRateCards.AnyAsync(c => c.Id == planCard.Value && c.IsActive, ct))
        {
            result.Add(new RatingCard(planCard.Value, RateSource.Plan));
        }

        var defaultCard = await _db.BillingRateCards.AsNoTracking()
            .Where(c => c.IsDefault && c.IsActive).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (defaultCard.HasValue && result.All(r => r.CardId != defaultCard.Value)) result.Add(new RatingCard(defaultCard.Value, RateSource.Default));

        return result;
    }

    public async Task<IReadOnlyList<BillingRate>> ListCandidateRatesAsync(IReadOnlyCollection<Guid> cardIds, string eventType,
        DateTimeOffset at, string? countryCode, string? @operator, string? provider, string? responsibility, bool exactResponsibility,
        CancellationToken ct = default) =>
        await _db.BillingRates.AsNoTracking()
            .Where(r => cardIds.Contains(r.RateCardId) && r.EventType == eventType
                        && r.EffectiveFrom <= at && (r.EffectiveTo == null || r.EffectiveTo > at)
                        && (r.CountryCode == null || r.CountryCode == countryCode)
                        && (r.Operator == null || r.Operator == @operator)
                        && (r.Provider == null || r.Provider == provider)
                        && (exactResponsibility
                            ? r.ProviderBilling == responsibility
                            : r.ProviderBilling == null || r.ProviderBilling == responsibility))
            .ToListAsync(ct);
}
