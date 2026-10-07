using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>Campaigns, templates, procedures and the knowledge base across clinics, for the admin portal.</summary>
public interface IContentAdminService
{
    Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clinicId, string? status, int page, CancellationToken ct);

    Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct);

    Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct);

    Task<List<TemplateRow>> ListTemplatesAsync(Guid? clinicId, string? status, CancellationToken ct);

    Task<List<ProcedureRow>> ListProceduresAsync(Guid? clinicId, CancellationToken ct);

    Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clinicId, string? search, bool? active, int page, CancellationToken ct);

    Task<KnowledgeDocDetail?> GetDocumentAsync(Guid id, CancellationToken ct);

    Task<SearchSettingsDetail?> SearchSettingsAsync(Guid clinicId, CancellationToken ct);

    Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clinicId, CancellationToken ct);

    Task<PlatformAdminChange> CancelCampaignAsync(Guid id, CancellationToken ct);

    Task<PlatformAdminChange> SetProcedureActiveAsync(Guid id, bool active, CancellationToken ct);

    Task<PlatformAdminChange> SetDocumentActiveAsync(Guid id, bool active, CancellationToken ct);

    /// <summary>Changes only Top K and minimum similarity; the clinic's chunking settings stay as they are.</summary>
    Task<PlatformAdminChange> UpdateSearchSettingsAsync(Guid clinicId, int topK, double minimumSimilarity, CancellationToken ct);
}
