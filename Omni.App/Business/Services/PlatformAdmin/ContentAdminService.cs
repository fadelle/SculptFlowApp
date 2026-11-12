using Omni.App.Common.Helpers;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Contracts.Services.Catalog;
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
    private readonly ICatalogService _services;
    private readonly IKnowledgeService _knowledge;
    private readonly IKnowledgeSettingsService _knowledgeSettings;
    private readonly IConfigManager _config;
    private readonly Omni.App.Business.Contracts.Jobs.IKnowledgeReindexQueue? _reindex;
    private readonly Microsoft.Extensions.Localization.IStringLocalizer? _text;

    public ContentAdminService(IContentAdminRepository repository, ICampaignService campaigns, ICatalogService services,
        IKnowledgeService knowledge, IKnowledgeSettingsService knowledgeSettings, IConfigManager config,
        Microsoft.Extensions.Localization.IStringLocalizer? text = null, Omni.App.Business.Contracts.Jobs.IKnowledgeReindexQueue? reindex = null)
    {
        _reindex = reindex;
        _repository = repository;
        _campaigns = campaigns;
        _services = services;
        _knowledge = knowledge;
        _knowledgeSettings = knowledgeSettings;
        _config = config;
        _text = text;
    }

    public Task<PagedResponse<CampaignRow>> ListCampaignsAsync(Guid? clientId, string? status, int page, CancellationToken ct) =>
        _repository.ListCampaignsAsync(clientId, status, page, ct);

    public Task<CampaignRow?> GetCampaignAsync(Guid id, CancellationToken ct) => _repository.GetCampaignAsync(id, ct);

    public async Task<PagedResponse<RecipientRow>> RecipientsAsync(Guid campaignId, string? status, int page, CancellationToken ct)
    {
        var rows = await _repository.RecipientsAsync(campaignId, status, page, ct);
        // A failure stored as a strings-file key (QA t5-ops-12) reads as text in the portal (English: admin requests carry no language).
        return _text is null ? rows : rows with { Items = rows.Items.Select(r => r with { FailureReason = StoredText.Show(r.FailureReason, _text) }).ToList() };
    }

    public Task<List<TemplateRow>> ListTemplatesAsync(Guid? clientId, string? status, CancellationToken ct) =>
        _repository.ListTemplatesAsync(clientId, status, ct);

    public Task<List<ServiceRow>> ListServicesAsync(Guid? clientId, CancellationToken ct) => _repository.ListServicesAsync(clientId, ct);

    public Task<PagedResponse<KnowledgeDocRow>> ListDocumentsAsync(Guid? clientId, string? search, bool? active, int page, CancellationToken ct) =>
        _repository.ListDocumentsAsync(clientId, search, active, page, ct);

    public async Task<KnowledgeDocDetail?> GetDocumentAsync(Guid id, CancellationToken ct) =>
        await _repository.GetDocumentAsync(id, ct) is { } doc ? PlatformAdminMapper.ToDetail(doc) : null;

    public async Task<SearchSettingsDetail?> SearchSettingsAsync(Guid clientId, CancellationToken ct) =>
        await _repository.SearchSettingsAsync(clientId, ct) is { } s
            ? PlatformAdminMapper.ToDetail(s) with { MinTopK = _config.KnowledgeMinTopK, MaxTopK = _config.KnowledgeMaxTopK }
            : null;

    public Task<List<WebsiteSourceRow>> ListWebsitesAsync(Guid? clientId, CancellationToken ct) => _repository.ListWebsitesAsync(clientId, ct);

    public async Task<PlatformAdminChange> CancelCampaignAsync(Guid id, CancellationToken ct)
    {
        var campaign = await _repository.GetCampaignAsync(id, ct) ?? throw new KeyNotFoundException("Campaign not found.");
        _ = await _campaigns.CancelAsync(campaign.ClientId, id, ct) ?? throw new KeyNotFoundException("Campaign not found.");
        return new PlatformAdminChange(campaign.ClientId);
    }

    public async Task<PlatformAdminChange> SetServiceActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var service = await _repository.GetServiceAsync(id, ct) ?? throw new KeyNotFoundException("Service not found.");
        await _services.SetActiveAsync(service.ClientId, id, active, ct);
        return new PlatformAdminChange(service.ClientId);
    }

    public async Task<PlatformAdminChange> SetDocumentActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var doc = await _repository.GetDocumentAsync(id, ct) ?? throw new KeyNotFoundException("Document not found.");
        await _knowledge.SetActiveAsync(doc.ClientId, id, active, ct);
        return new PlatformAdminChange(doc.ClientId);
    }

    public async Task<PlatformAdminChange> StartReindexAsync(Guid clientId, CancellationToken ct)
    {
        if (await _repository.SearchSettingsAsync(clientId, ct) is null) throw new KeyNotFoundException("This client has no Knowledge Base.");
        if (_reindex is null || !_reindex.Start(clientId, DateTimeOffset.UtcNow))
            throw new InvalidOperationException("A re-index is already running for this client.");
        return new PlatformAdminChange(clientId);
    }

    public Omni.App.Entities.Dtos.Knowledge.KnowledgeReindexProgress? ReindexProgress(Guid clientId) => _reindex?.Progress(clientId);

    public async Task<PlatformAdminChange> UpdateSearchSettingsAsync(Guid clientId, int topK, double minimumSimilarity, CancellationToken ct)
    {
        var current = await _repository.SearchSettingsAsync(clientId, ct)
                      ?? throw new InvalidOperationException("This client has no knowledge search settings row yet.");
        await _knowledgeSettings.UpdateAsync(clientId,
            new UpdateKnowledgeSettingsRequest(current.ChunkSizeTokens, current.ChunkOverlapTokens, topK, minimumSimilarity), ct);
        return new PlatformAdminChange(clientId);
    }
}
