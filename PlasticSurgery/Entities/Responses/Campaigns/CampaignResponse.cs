using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Responses.Campaigns;

public record CampaignResponse(
    Guid Id,
    Guid ClinicId,
    string Name,
    string CampaignType,
    string Channel,
    Guid? WhatsAppTemplateId,
    string? TemplateName,
    string AudienceType,
    string? AudienceFilters,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
