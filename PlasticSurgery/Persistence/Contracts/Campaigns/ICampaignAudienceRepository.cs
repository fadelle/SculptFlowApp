using PlasticSurgery.Entities.Dtos.Campaigns;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Campaigns;

/// <summary>
/// The campaign audience query: mandatory exclusions (always) + the audience type's rules. This is the ONLY place
/// eligibility is computed; ICampaignAudienceService is its front door.
/// </summary>
public interface ICampaignAudienceRepository
{
    /// <summary>Tracked leads matching the audience.</summary>
    Task<List<Lead>> ListEligibleLeadsAsync(Guid clinicId, string audienceType, CampaignAudienceFilters filters,
        CancellationToken ct = default);

    Task<int> CountEligibleLeadsAsync(Guid clinicId, string audienceType, CampaignAudienceFilters filters,
        CancellationToken ct = default);
}
