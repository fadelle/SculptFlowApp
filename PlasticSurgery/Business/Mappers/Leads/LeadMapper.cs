using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Leads;

namespace PlasticSurgery.Business.Mappers.Leads;

public static class LeadMapper
{
    /// <summary>Expects lead.Procedure to be loaded when the lead has a procedure.</summary>
    public static LeadResponse ToResponse(Lead l) => new(
        l.Id, l.ClinicId, l.ProcedureId, l.Procedure?.Name,
        l.FullName, l.FirstName, l.LastName, l.Phone, l.Email,
        l.Source, l.SourceDetail, l.CampaignName, l.ExternalLeadId,
        l.Status, l.QualificationStatus, l.PreferredLanguage, l.City, l.DesiredTimeline, l.Notes,
        l.MarketingOptIn, l.OptedOutAt, l.LastContactAt, l.NextFollowupAt, l.CreatedAt, l.UpdatedAt
    );
}
