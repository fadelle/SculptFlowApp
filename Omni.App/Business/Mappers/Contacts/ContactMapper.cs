using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Contacts;

namespace Omni.App.Business.Mappers.Contacts;

public static class ContactMapper
{
    /// <summary>Expects contact.Service to be loaded when the contact has a service.</summary>
    public static ContactResponse ToResponse(Contact l) => new(
        l.Id, l.ClientId, l.ServiceId, l.Service?.Name,
        l.FullName, l.FirstName, l.LastName, l.Phone, l.Email,
        l.Source, l.SourceDetail, l.CampaignName, l.ExternalContactId,
        l.StageKey, l.Qualification, l.PreferredLanguage, l.City, l.DesiredTimeline, l.Notes,
        l.MarketingOptIn, l.OptedOutAt, l.LastContactAt, l.NextFollowupAt, l.CreatedAt, l.UpdatedAt
    );
}
