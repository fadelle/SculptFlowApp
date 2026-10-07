using PlasticSurgery.Business.Contracts.Services.Campaigns;
using PlasticSurgery.Business.Contracts.Services.Knowledge;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Business.Contracts.Services.Procedures;
using PlasticSurgery.Business.Mappers.PlatformAdmin;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Requests.Knowledge;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

/// <summary>Reads come from the admin repository; every write goes through the clinic's own service and its rules.</summary>
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

    public Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clinicId, string? status, int page, CancellationToken ct) =>
        _repository.ListCampaignsAsync(clinicId, status, page, ct);

    public Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct) => _repository.GetCampaignAsync(id, ct);

    public Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct) =>
        _repository.RecipientsAsync(campaignId, status, page, ct);

    public Task<List<TemplateRow>> ListTemplatesAsync(Guid? clinicId, string? status, CancellationToken ct) =>
        _repository.ListTemplatesAsync(clinicId, status, ct);

    public Task<List<ProcedureRow>> ListProceduresAsync(Guid? clinicId, CancellationToken ct) => _repository.ListProceduresAsync(clinicId, ct);

    public Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clinicId, string? search, bool? active, int page, CancellationToken ct) =>
        _repository.ListDocumentsAsync(clinicId, search, active, page, ct);

    public async Task<KnowledgeDocDetail?> GetDocumentAsync(Guid id, CancellationToken ct) =>
        await _repository.GetDocumentAsync(id, ct) is { } doc ? PlatformAdminMapper.ToDetail(doc) : null;

    public async Task<SearchSettingsDetail?> SearchSettingsAsync(Guid clinicId, CancellationToken ct) =>
        await _repository.SearchSettingsAsync(clinicId, ct) is { } s ? PlatformAdminMapper.ToDetail(s) : null;

    public Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clinicId, CancellationToken ct) => _repository.ListWebsitesAsync(clinicId, ct);

    public async Task<PlatformAdminChange> CancelCampaignAsync(Guid id, CancellationToken ct)
    {
        var campaign = await _repository.GetCampaignAsync(id, ct) ?? throw new KeyNotFoundException("Campaign not found.");
        _ = await _campaigns.CancelAsync(campaign.ClinicId, id, ct) ?? throw new KeyNotFoundException("Campaign not found.");
        return new PlatformAdminChange(campaign.ClinicId);
    }

    public async Task<PlatformAdminChange> SetProcedureActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var procedure = await _repository.GetProcedureAsync(id, ct) ?? throw new KeyNotFoundException("Procedure not found.");
        await _procedures.SetActiveAsync(procedure.ClinicId, id, active, ct);
        return new PlatformAdminChange(procedure.ClinicId);
    }

    public async Task<PlatformAdminChange> SetDocumentActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var doc = await _repository.GetDocumentAsync(id, ct) ?? throw new KeyNotFoundException("Document not found.");
        await _knowledge.SetActiveAsync(doc.ClinicId, id, active, ct);
        return new PlatformAdminChange(doc.ClinicId);
    }

    public async Task<PlatformAdminChange> UpdateSearchSettingsAsync(Guid clinicId, int topK, double minimumSimilarity, CancellationToken ct)
    {
        var current = await _repository.SearchSettingsAsync(clinicId, ct)
                      ?? throw new InvalidOperationException("This clinic has no knowledge search settings row yet.");
        await _knowledgeSettings.UpdateAsync(clinicId,
            new UpdateKnowledgeSettingsRequest(current.ChunkSizeTokens, current.ChunkOverlapTokens, topK, minimumSimilarity), ct);
        return new PlatformAdminChange(clinicId);
    }
}
