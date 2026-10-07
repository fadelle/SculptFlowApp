using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Persistence.Contracts.PlatformAdmin;

public interface IContentAdminRepository
{
    Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clinicId, string? status, int page, CancellationToken ct = default);

    Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct = default);

    Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct = default);

    Task<List<TemplateRow>> ListTemplatesAsync(Guid? clinicId, string? status, CancellationToken ct = default);

    Task<List<ProcedureRow>> ListProceduresAsync(Guid? clinicId, CancellationToken ct = default);

    Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clinicId, string? search, bool? active, int page,
        CancellationToken ct = default);

    Task<KnowledgeDocument?> GetDocumentAsync(Guid id, CancellationToken ct = default);

    Task<KnowledgeSearchSettings?> SearchSettingsAsync(Guid clinicId, CancellationToken ct = default);

    Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clinicId, CancellationToken ct = default);

    Task<Procedure?> GetProcedureAsync(Guid id, CancellationToken ct = default);

}
