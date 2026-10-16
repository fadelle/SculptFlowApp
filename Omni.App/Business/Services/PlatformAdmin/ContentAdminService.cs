using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Contracts.Services.Procedures;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

/// <summary>Reads come from the admin repository; every write goes through the client's own service and its rules.</summary>
public class ContentAdminService : IContentAdminService
{
    private readonly IContentAdminRepository _repository;
    private readonly ICampaignService _campaigns;
    private readonly IProcedureService _procedures;
    private readonly IKnowledgeService _knowledge;
    private readonly IKnowledgeSettingsService _knowledgeSettings;

    public ContentAdminService(IContentAdminRepository repository, ICampaignService campaigns, IProcedureService procedures,
        IKnowledgeService knowledge, IKnowledgeSettingsService knowledgeSettings)
    {
        _repository = repository;
        _campaigns = campaigns;
        _procedures = procedures;
        _knowledge = knowledge;
        _knowledgeSettings = knowledgeSettings;
    }

    public Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clientId, string? status, int page, CancellationToken ct) =>
        _repository.ListCampaignsAsync(clientId, status, page, ct);

    public Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct) => _repository.GetCampaignAsync(id, ct);

    public Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct) =>
        _repository.RecipientsAsync(campaignId, status, page, ct);

    public Task<List<TemplateRow>> ListTemplatesAsync(Guid? clientId, string? status, CancellationToken ct) =>
        _repository.ListTemplatesAsync(clientId, status, ct);

    public Task<List<ProcedureRow>> ListProceduresAsync(Guid? clientId, CancellationToken ct) => _repository.ListProceduresAsync(clientId, ct);

    public Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clientId, string? search, bool? active, int page, CancellationToken ct) =>
        _repository.ListDocumentsAsync(clientId, search, active, page, ct);

    public async Task<KnowledgeDocDetail?> GetDocumentAsync(Guid id, CancellationToken ct) =>
        await _repository.GetDocumentAsync(id, ct) is { } doc ? PlatformAdminMapper.ToDetail(doc) : null;

    public async Task<SearchSettingsDetail?> SearchSettingsAsync(Guid clientId, CancellationToken ct) =>
        await _repository.SearchSettingsAsync(clientId, ct) is { } s ? PlatformAdminMapper.ToDetail(s) : null;

    public Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clientId, CancellationToken ct) => _repository.ListWebsitesAsync(clientId, ct);

    public async Task<PlatformAdminChange> CancelCampaignAsync(Guid id, CancellationToken ct)
    {
        var campaign = await _repository.GetCampaignAsync(id, ct) ?? throw new KeyNotFoundException("Campaign not found.");
        _ = await _campaigns.CancelAsync(campaign.ClientId, id, ct) ?? throw new KeyNotFoundException("Campaign not found.");
        return new PlatformAdminChange(campaign.ClientId);
    }

    public async Task<PlatformAdminChange> SetProcedureActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var procedure = await _repository.GetProcedureAsync(id, ct) ?? throw new KeyNotFoundException("Procedure not found.");
        await _procedures.SetActiveAsync(procedure.ClientId, id, active, ct);
        return new PlatformAdminChange(procedure.ClientId);
    }

    public async Task<PlatformAdminChange> SetDocumentActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var doc = await _repository.GetDocumentAsync(id, ct) ?? throw new KeyNotFoundException("Document not found.");
        await _knowledge.SetActiveAsync(doc.ClientId, id, active, ct);
        return new PlatformAdminChange(doc.ClientId);
    }

    public async Task<PlatformAdminChange> UpdateSearchSettingsAsync(Guid clientId, int topK, double minimumSimilarity, CancellationToken ct)
    {
        var current = await _repository.SearchSettingsAsync(clientId, ct)
                      ?? throw new InvalidOperationException("This client has no knowledge search settings row yet.");
        await _knowledgeSettings.UpdateAsync(clientId,
            new UpdateKnowledgeSettingsRequest(current.ChunkSizeTokens, current.ChunkOverlapTokens, topK, minimumSimilarity), ct);
        return new PlatformAdminChange(clientId);
    }
}
