using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.Billing;

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
