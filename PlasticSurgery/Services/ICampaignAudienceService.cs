using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>
/// The single place that turns a Campaign's AudienceType + AudienceFilters into an actual list of
/// leads. Centralized so every audience type — including a future one — automatically respects the
/// same mandatory contactability exclusions, and so "old lead reactivation" eligibility is computed
/// from real lead/conversation/appointment activity rather than a stored flag on Lead.
/// </summary>
public interface ICampaignAudienceService
{
    /// <summary>Resolves the actual lead list for one audience type. Called once, at campaign
    /// creation time, to snapshot CampaignRecipient rows — never re-run later to "refresh" an
    /// existing campaign's recipients.</summary>
    Task<List<Lead>> GetEligibleLeadsAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default);

    /// <summary>Same resolution, count only — backs the "Matching Leads: N" preview so the dashboard
    /// doesn't have to materialize (or the caller discard) the full lead list just to show a number.</summary>
    Task<int> GetMatchingCountAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default);

    /// <summary>Mandatory exclusions that apply regardless of audience type or filters — do_not_contact
    /// equivalent (MarketingOptIn/OptedOutAt), missing/blank phone. Exposed so callers that build a
    /// recipient list from an explicit lead selection (not an audience type) can still apply the same
    /// rule instead of a second copy of it. Returns null if contactable, else a short skip-reason code.</summary>
    string? GetSkipReasonIfNotContactable(Lead lead);
}
