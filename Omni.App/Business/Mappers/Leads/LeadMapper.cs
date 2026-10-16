using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Leads;

namespace Omni.App.Business.Mappers.Leads;

public static class LeadMapper
{
    /// <summary>Expects lead.Service to be loaded when the lead has a service.</summary>
    public static LeadResponse ToResponse(Lead l) => new(
        l.Id, l.ClientId, l.ServiceId, l.Service?.Name,
        l.FullName, l.FirstName, l.LastName, l.Phone, l.Email,
        l.Source, l.SourceDetail, l.CampaignName, l.ExternalLeadId,
        l.Status, l.QualificationStatus, l.PreferredLanguage, l.City, l.DesiredTimeline, l.Notes,
        l.MarketingOptIn, l.OptedOutAt, l.LastContactAt, l.NextFollowupAt, l.CreatedAt, l.UpdatedAt
    );
}
