using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Campaigns;

namespace Omni.App.Business.Services.Campaigns;

public class CampaignAudienceService : ICampaignAudienceService
{
    private readonly ICampaignAudienceRepository _audience;
    private readonly IConfigManager _config;

    public CampaignAudienceService(ICampaignAudienceRepository audience, IConfigManager config)
    {
        _config = config;
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
        _audience.ListEligibleLeadsAsync(clinicId, audienceType, CampaignAudienceFilters.Parse(filtersJson), _config.CampaignsDefaultInactiveDays, ct);

    public Task<int> GetMatchingCountAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default) =>
        _audience.CountEligibleLeadsAsync(clinicId, audienceType, CampaignAudienceFilters.Parse(filtersJson), _config.CampaignsDefaultInactiveDays, ct);
}
