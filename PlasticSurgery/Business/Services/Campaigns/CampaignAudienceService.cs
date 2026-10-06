using PlasticSurgery.Business.Contracts.Services.Campaigns;
using PlasticSurgery.Entities.Dtos.Campaigns;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Campaigns;

namespace PlasticSurgery.Business.Services.Campaigns;

public class CampaignAudienceService : ICampaignAudienceService
{
    private readonly ICampaignAudienceRepository _audience;

    public CampaignAudienceService(ICampaignAudienceRepository audience)
    {
        _audience = audience;
    }

    public string? GetSkipReasonIfNotContactable(Lead lead)
    {
        if (string.IsNullOrWhiteSpace(lead.Phone)) return "no_phone";
        if (lead.OptedOutAt.HasValue) return "opted_out";
        if (!lead.MarketingOptIn) return "marketing_opt_in_false";
        return null;
    }

    public Task<List<Lead>> GetEligibleLeadsAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default) =>
        _audience.ListEligibleLeadsAsync(clinicId, audienceType, CampaignAudienceFilters.Parse(filtersJson), ct);

    public Task<int> GetMatchingCountAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default) =>
        _audience.CountEligibleLeadsAsync(clinicId, audienceType, CampaignAudienceFilters.Parse(filtersJson), ct);
}
