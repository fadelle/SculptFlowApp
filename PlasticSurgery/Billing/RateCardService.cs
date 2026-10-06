using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Billing;

/// <summary>
/// Rate cards and their rate versions (admin API). Versioning: a rate is never edited. Adding a rate for a key
/// (card, event type, country, operator, provider, provider billing) that already has a current version closes that version at the
/// new one's start; history stays queryable and usage keeps the price it was rated with. Rates can't start in the
/// past, so a change can never re-price usage that already happened. Cards are never deleted, only deactivated.
/// </summary>
public interface IRateCardService
{
    Task<IReadOnlyList<RateCardResponse>> ListAsync(CancellationToken ct = default);
    Task<RateCardResponse> CreateAsync(RateCardRequest request, CancellationToken ct = default);
    Task<RateCardResponse?> UpdateAsync(string code, RateCardUpdateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RateResponse>?> ListRatesAsync(string cardCode, bool includeHistory, CancellationToken ct = default);
    Task<RateResponse?> AddRateAsync(string cardCode, AddRateRequest request, string? actor, CancellationToken ct = default);
    /// <summary>Ends a rate version (default: now). After that, events it covered need another rate.</summary>
    Task<RateResponse?> CloseRateAsync(Guid rateId, DateTimeOffset? effectiveTo, CancellationToken ct = default);
}

public partial class RateCardService : IRateCardService
{
    /// <summary>A new version may start this far in the past (clock skew between the admin tool and the server).</summary>
    private static readonly TimeSpan BackdateTolerance = TimeSpan.FromMinutes(5);

    private readonly BillingDbFactory _dbFactory;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RateCardService> _logger;

    public RateCardService(BillingDbFactory dbFactory, IOptions<BillingOptions> options, TimeProvider time, ILogger<RateCardService> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RateCardResponse>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var now = _time.GetUtcNow();
        var cards = await db.BillingRateCards.AsNoTracking().OrderByDescending(c => c.IsDefault).ThenBy(c => c.Code).ToListAsync(ct);
        var counts = await db.BillingRates.AsNoTracking()
            .Where(r => r.EffectiveFrom <= now && (r.EffectiveTo == null || r.EffectiveTo > now))
            .GroupBy(r => r.RateCardId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return cards.Select(c => ToResponse(c, counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<RateCardResponse> CreateAsync(RateCardRequest request, CancellationToken ct = default)
    {
        var code = (request.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(code)) throw new ArgumentException("Rate card code: 1-50 lowercase letters, digits, '-' or '_'.");
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("Rate card name is required (max 100 characters).");
        if (request.IsDefault && request.ClinicId is not null) throw new ArgumentException("A clinic's own card can't be the default card.");

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        if (await db.BillingRateCards.AnyAsync(c => c.Code == code, ct)) throw new ArgumentException($"Rate card '{code}' already exists.");
        if (request.ClinicId is Guid clinicId)
        {
            if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct)) throw new ArgumentException("Clinic not found.");
            if (await db.BillingRateCards.AnyAsync(c => c.ClinicId == clinicId, ct)) throw new ArgumentException("This clinic already has its own rate card.");
        }

        var now = _time.GetUtcNow();
        if (request.IsDefault) await UnsetDefaultAsync(db, now, ct);
        var card = new BillingRateCard
        {
            Id = Guid.NewGuid(), Code = code, Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ClinicId = request.ClinicId, IsDefault = request.IsDefault, IsActive = true, CreatedAt = now, UpdatedAt = now
        };
        db.BillingRateCards.Add(card);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: rate card {Card} created (default {Default}, clinic {ClinicId}).", code, card.IsDefault, card.ClinicId);
        return ToResponse(card, 0);
    }

    public async Task<RateCardResponse?> UpdateAsync(string code, RateCardUpdateRequest request, CancellationToken ct = default)
    {
        code = (code ?? string.Empty).Trim().ToLowerInvariant();
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("Rate card name is required (max 100 characters).");

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var card = await db.BillingRateCards.FirstOrDefaultAsync(c => c.Code == code, ct);
        if (card is null) return null;
        if (request.IsDefault && card.ClinicId is not null) throw new ArgumentException("A clinic's own card can't be the default card.");

        var now = _time.GetUtcNow();
        if (request.IsDefault && !card.IsDefault)
        {
            await UnsetDefaultAsync(db, now, ct);
        }
        card.Name = name;
        card.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        card.IsActive = request.IsActive;
        card.IsDefault = request.IsDefault;
        card.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: rate card {Card} updated (active {Active}, default {Default}).", code, card.IsActive, card.IsDefault);
        return (await ListAsync(ct)).FirstOrDefault(c => c.Code == code);
    }

    public async Task<IReadOnlyList<RateResponse>?> ListRatesAsync(string cardCode, bool includeHistory, CancellationToken ct = default)
    {
        cardCode = (cardCode ?? string.Empty).Trim().ToLowerInvariant();
        await using var db = _dbFactory.Create();
        var card = await db.BillingRateCards.AsNoTracking().FirstOrDefaultAsync(c => c.Code == cardCode, ct);
        if (card is null) return null;

        var now = _time.GetUtcNow();
        var query = db.BillingRates.AsNoTracking().Where(r => r.RateCardId == card.Id);
        if (!includeHistory) query = query.Where(r => r.EffectiveTo == null || r.EffectiveTo > now);
        var rates = await query.OrderBy(r => r.EventType).ThenBy(r => r.CountryCode).ThenBy(r => r.Provider).ThenBy(r => r.EffectiveFrom).ToListAsync(ct);
        return rates.Select(r => ToResponse(r, card.Code)).ToList();
    }

    public async Task<RateResponse?> AddRateAsync(string cardCode, AddRateRequest request, string? actor, CancellationToken ct = default)
    {
        cardCode = (cardCode ?? string.Empty).Trim().ToLowerInvariant();
        var eventType = (request.EventType ?? string.Empty).Trim().ToLowerInvariant();
        if (!BillableEventTypes.IsValid(eventType)) throw new ArgumentException("Event type: lowercase letters, digits and '_' (e.g. whatsapp_marketing_message).");
        var country = RateResolver.NormalizeCountry(request.CountryCode);
        if (country is not null && !CountryPattern().IsMatch(country)) throw new ArgumentException("Country must be a 2-letter ISO code (e.g. LB), or empty for any.");
        var op = RateResolver.NormalizeDimension(request.Operator);
        var provider = RateResolver.NormalizeDimension(request.Provider);
        if (op is { Length: > 60 } || provider is { Length: > 30 }) throw new ArgumentException("Operator is max 60 and provider max 30 characters.");
        var providerBilling = string.IsNullOrWhiteSpace(request.ProviderBilling) ? null : request.ProviderBilling.Trim().ToLowerInvariant();
        if (providerBilling is not null && !ProviderBillingResponsibility.IsValid(providerBilling))
        {
            throw new ArgumentException($"Provider billing must be empty (SculptFlow-funded pricing) or one of: {string.Join(", ", ProviderBillingResponsibility.All)}.");
        }
        if (request.ClientRate < 0 || request.ProviderCost < 0) throw new ArgumentException("Prices can't be negative.");
        if (decimal.Round(request.ClientRate, 6) != request.ClientRate || decimal.Round(request.ProviderCost, 6) != request.ProviderCost)
        {
            throw new ArgumentException("Prices have at most 6 decimals.");
        }

        var now = _time.GetUtcNow();
        var from = request.EffectiveFrom ?? now;
        if (from < now - BackdateTolerance) throw new ArgumentException("A rate can't start in the past — that would re-price usage that already happened.");
        if (request.EffectiveTo is DateTimeOffset requestedEnd && requestedEnd <= from) throw new ArgumentException("EffectiveTo must be after EffectiveFrom.");

        await using var db = _dbFactory.Create();
        await using var tx = await BillingStore.BeginAsync(db, ct);
        var card = await db.BillingRateCards.FirstOrDefaultAsync(c => c.Code == cardCode, ct);
        if (card is null) return null;
        // Serializes concurrent version changes of this card (the exclusion constraint is the final guard).
        await db.Database.ExecuteSqlInterpolatedAsync($"select 1 from billing.rate_cards where id = {card.Id} for update", ct);

        var versions = await db.BillingRates
            .Where(r => r.RateCardId == card.Id && r.EventType == eventType && r.CountryCode == country && r.Operator == op && r.Provider == provider
                        && r.ProviderBilling == providerBilling)
            .ToListAsync(ct);
        if (versions.Any(v => v.EffectiveFrom == from)) throw new ArgumentException("A version of this rate already starts at that moment.");

        var current = versions.FirstOrDefault(v => v.EffectiveFrom < from && (v.EffectiveTo is null || v.EffectiveTo > from));
        var next = versions.Where(v => v.EffectiveFrom > from).OrderBy(v => v.EffectiveFrom).FirstOrDefault();
        var to = request.EffectiveTo ?? next?.EffectiveFrom;
        if (next is not null && to > next.EffectiveFrom) throw new ArgumentException("That period overlaps a later version of this rate.");
        if (current is not null) current.EffectiveTo = from;

        var rate = new BillingRate
        {
            Id = Guid.NewGuid(),
            RateCardId = card.Id,
            EventType = eventType,
            CountryCode = country,
            Operator = op,
            Provider = provider,
            ProviderBilling = providerBilling,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "unit" : request.Unit.Trim().ToLowerInvariant(),
            ProviderCost = request.ProviderCost,
            ClientRate = request.ClientRate,
            Currency = _options.NormalizedCurrency,
            EffectiveFrom = from,
            EffectiveTo = to,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = BillingStore.Truncate(actor, 200),
            CreatedAt = now
        };
        db.BillingRates.Add(rate);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ExclusionViolation })
        {
            throw new ArgumentException("That period overlaps another version of this rate.");
        }
        await tx.CommitAsync(ct);

        _logger.LogInformation("Billing: rate {EventType} ({Country}/{Operator}/{Provider}) on card {Card} set to {ClientRate} (cost {ProviderCost}) {Currency} from {From} by {Actor}{Closed}.",
            eventType, country ?? "any", op ?? "any", provider ?? "any", card.Code, rate.ClientRate, rate.ProviderCost, rate.Currency, from,
            actor ?? "admin", current is null ? "" : $"; previous version {current.Id} closed");
        return ToResponse(rate, card.Code);
    }

    public async Task<RateResponse?> CloseRateAsync(Guid rateId, DateTimeOffset? effectiveTo, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var end = effectiveTo ?? now;
        if (end < now - BackdateTolerance) throw new ArgumentException("A rate can't be ended in the past.");

        await using var db = _dbFactory.Create();
        var rate = await db.BillingRates.FirstOrDefaultAsync(r => r.Id == rateId, ct);
        if (rate is null) return null;
        if (end <= rate.EffectiveFrom) throw new ArgumentException("The end must be after the rate's start.");
        if (rate.EffectiveTo is not null && rate.EffectiveTo <= now) throw new ArgumentException("This rate version has already ended.");
        rate.EffectiveTo = end;
        await db.SaveChangesAsync(ct);

        var cardCode = await db.BillingRateCards.Where(c => c.Id == rate.RateCardId).Select(c => c.Code).FirstAsync(ct);
        _logger.LogInformation("Billing: rate {RateId} ({EventType}) on card {Card} ends at {End}.", rate.Id, rate.EventType, cardCode, end);
        return ToResponse(rate, cardCode);
    }

    private static async Task UnsetDefaultAsync(Data.ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var other in await db.BillingRateCards.Where(c => c.IsDefault).ToListAsync(ct))
        {
            other.IsDefault = false;
            other.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct); // before the new default is written: the partial unique index allows one at a time
    }

    private static RateCardResponse ToResponse(BillingRateCard c, int currentRates) =>
        new(c.Id, c.Code, c.Name, c.Description, c.ClinicId, c.IsDefault, c.IsActive, currentRates);

    internal static RateResponse ToResponse(BillingRate r, string cardCode) =>
        new(r.Id, cardCode, r.EventType, r.CountryCode, r.Operator, r.Provider, r.ProviderBilling, r.Unit, r.ProviderCost, r.ClientRate,
            // A fee rate for customer-paid accounts: SculptFlow doesn't pay the provider cost, so the whole fee is margin.
            r.ProviderBilling is null or ProviderBillingResponsibility.PlatformFunded ? r.ClientRate - r.ProviderCost : r.ClientRate,r.Currency.Trim(), r.EffectiveFrom, r.EffectiveTo, r.Notes, r.CreatedBy, r.CreatedAt);

    [GeneratedRegex("^[a-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryPattern();
}
