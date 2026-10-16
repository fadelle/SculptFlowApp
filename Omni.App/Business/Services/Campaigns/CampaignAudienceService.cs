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

    public string? GetSkipReasonIfNotContactable(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.Phone)) return "no_phone";
        if (contact.OptedOutAt.HasValue) return "opted_out";
        if (!contact.MarketingOptIn) return "marketing_opt_in_false";
        return null;
    }

    public Task<List<Contact>> GetEligibleContactsAsync(Guid clientId, string audienceType, string? filtersJson, CancellationToken ct = default) =>
        _audience.ListEligibleContactsAsync(clientId, audienceType, CampaignAudienceFilters.Parse(filtersJson), _config.CampaignsDefaultInactiveDays, ct);

    public Task<int> GetMatchingCountAsync(Guid clientId, string audienceType, string? filtersJson, CancellationToken ct = default) =>
        _audience.CountEligibleContactsAsync(clientId, audienceType, CampaignAudienceFilters.Parse(filtersJson), _config.CampaignsDefaultInactiveDays, ct);
}
