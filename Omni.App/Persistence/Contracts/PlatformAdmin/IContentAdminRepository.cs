using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface IContentAdminRepository
{
    Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clientId, string? status, int page, CancellationToken ct = default);

    Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct = default);

    Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct = default);

    Task<List<TemplateRow>> ListTemplatesAsync(Guid? clientId, string? status, CancellationToken ct = default);

    Task<List<ProcedureRow>> ListProceduresAsync(Guid? clientId, CancellationToken ct = default);

    Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clientId, string? search, bool? active, int page,
        CancellationToken ct = default);

    Task<KnowledgeDocument?> GetDocumentAsync(Guid id, CancellationToken ct = default);

    Task<KnowledgeSearchSettings?> SearchSettingsAsync(Guid clientId, CancellationToken ct = default);

    Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clientId, CancellationToken ct = default);

    Task<Procedure?> GetProcedureAsync(Guid id, CancellationToken ct = default);

}
