using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Campaigns;

/// <summary>
/// The campaign audience query: mandatory exclusions (always) + the audience type's rules. This is the ONLY place
/// eligibility is computed; ICampaignAudienceService is its front door.
/// </summary>
public interface ICampaignAudienceRepository
{
    /// <summary>Tracked contacts matching the audience. <paramref name="defaultInactiveDays"/> applies to a reactivation
    /// audience whose filters don't set inactiveDays.</summary>
    Task<List<Contact>> ListEligibleContactsAsync(Guid clientId, string audienceType, CampaignAudienceFilters filters, int defaultInactiveDays,
        CancellationToken ct = default);

    Task<int> CountEligibleContactsAsync(Guid clientId, string audienceType, CampaignAudienceFilters filters, int defaultInactiveDays,
        CancellationToken ct = default);
}
