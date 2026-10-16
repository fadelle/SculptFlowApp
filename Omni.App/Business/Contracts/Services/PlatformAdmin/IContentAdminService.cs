using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>Campaigns, templates, services and the knowledge base across clients, for the admin portal.</summary>
public interface IContentAdminService
{
    Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clientId, string? status, int page, CancellationToken ct);

    Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct);

    Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct);

    Task<List<TemplateRow>> ListTemplatesAsync(Guid? clientId, string? status, CancellationToken ct);

    Task<List<ServiceRow>> ListServicesAsync(Guid? clientId, CancellationToken ct);

    Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clientId, string? search, bool? active, int page, CancellationToken ct);

    Task<KnowledgeDocDetail?> GetDocumentAsync(Guid id, CancellationToken ct);

    Task<SearchSettingsDetail?> SearchSettingsAsync(Guid clientId, CancellationToken ct);

    Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clientId, CancellationToken ct);

    Task<PlatformAdminChange> CancelCampaignAsync(Guid id, CancellationToken ct);

    Task<PlatformAdminChange> SetServiceActiveAsync(Guid id, bool active, CancellationToken ct);

    Task<PlatformAdminChange> SetDocumentActiveAsync(Guid id, bool active, CancellationToken ct);

    /// <summary>Changes only Top K and minimum similarity; the client's chunking settings stay as they are.</summary>
    Task<PlatformAdminChange> UpdateSearchSettingsAsync(Guid clientId, int topK, double minimumSimilarity, CancellationToken ct);
}
