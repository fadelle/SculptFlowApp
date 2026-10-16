using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Engines.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Knowledge;

namespace Omni.App.Business.Services.Knowledge;

public class KnowledgeSettingsService : IKnowledgeSettingsService
{
    // Bounds for what a client may choose, read through IConfigManager (Knowledge section of ConfigDefaults).
    private int MinChunkSizeTokens => _config.KnowledgeMinChunkSizeTokens;
    private int MaxChunkSizeTokens => _config.KnowledgeMaxChunkSizeTokens;
    private int MinTopK => _config.KnowledgeMinTopK;
    private int MaxTopK => _config.KnowledgeMaxTopK;

    private readonly IKnowledgeSettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IConfigManager _config;

    public KnowledgeSettingsService(IKnowledgeSettingsRepository settings, IUnitOfWork unitOfWork, IConfiguration configuration, IConfigManager config)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _config = config;
    }

    public async Task<KnowledgeSettingsResponse> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        var row = await _settings.GetReadOnlyAsync(clientId, ct);
        if (row is not null) return ToResponse(row);

        var now = DateTimeOffset.UtcNow;
        var created = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            EmbeddingModel = _config.EmbeddingsModel,
            VectorDimension = _config.EmbeddingsDimensions,
            SimilarityMethod = KnowledgeSimilarityMethod.Cosine,
            VectorIndexType = KnowledgeVectorIndexType.None,
            ChunkSizeTokens = Math.Clamp(_config.KnowledgeChunkMaxChars / KnowledgeChunkingService.CharsPerToken, MinChunkSizeTokens, MaxChunkSizeTokens),
            ChunkOverlapTokens = (int)Math.Round(_config.KnowledgeChunkOverlapChars / (double)KnowledgeChunkingService.CharsPerToken),
            TopK = 5,
            MinimumSimilarity = (double)_config.KnowledgeMinScore,
            CreatedAt = now,
            UpdatedAt = now
        };
        created.ChunkOverlapTokens = Math.Clamp(created.ChunkOverlapTokens, 0, created.ChunkSizeTokens / 2);

        _settings.Add(created);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
            return ToResponse(created);
        }
        catch (DuplicateRecordException)
        {
            // Another request created this client's row first (unique client_id) — use theirs.
            _settings.Detach(created);
            var existing = await _settings.GetReadOnlyAsync(clientId, ct);
            return existing is null ? throw new InvalidOperationException("Couldn't create Knowledge Base settings.") : ToResponse(existing);
        }
    }

    public async Task<KnowledgeSettingsResponse> UpdateAsync(Guid clientId, UpdateKnowledgeSettingsRequest request, CancellationToken ct = default)
    {
        Validate(request);
        await GetAsync(clientId, ct); // make sure the row exists

        var row = await _settings.GetAsync(clientId, ct)
                  ?? throw new InvalidOperationException("Couldn't load Knowledge Base settings.");
        row.ChunkSizeTokens = request.ChunkSizeTokens;
        row.ChunkOverlapTokens = request.ChunkOverlapTokens;
        row.TopK = request.TopK;
        row.MinimumSimilarity = request.MinimumSimilarity;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    private void Validate(UpdateKnowledgeSettingsRequest r)
    {
        if (r.ChunkSizeTokens < MinChunkSizeTokens || r.ChunkSizeTokens > MaxChunkSizeTokens)
        {
            throw new ArgumentException($"Chunk size must be between {MinChunkSizeTokens} and {MaxChunkSizeTokens} tokens.");
        }
        if (r.ChunkOverlapTokens < 0 || r.ChunkOverlapTokens > r.ChunkSizeTokens / 2)
        {
            throw new ArgumentException("Chunk overlap must be between 0 and half the chunk size.");
        }
        if (r.TopK < MinTopK || r.TopK > MaxTopK)
        {
            throw new ArgumentException($"Top K must be between {MinTopK} and {MaxTopK}.");
        }
        if (double.IsNaN(r.MinimumSimilarity) || r.MinimumSimilarity is < 0 or > 1)
        {
            throw new ArgumentException("Minimum similarity must be between 0 and 1.");
        }
    }

    private string ConfigString(string key, string fallback) => _configuration[key] is { Length: > 0 } v ? v : fallback;

    private int ConfigInt(string key, int fallback) => int.TryParse(_configuration[key], out var v) && v > 0 ? v : fallback;

    private static KnowledgeSettingsResponse ToResponse(KnowledgeSearchSettings s) => new(
        s.EmbeddingModel, s.VectorDimension, s.SimilarityMethod, s.VectorIndexType,
        s.ChunkSizeTokens, s.ChunkOverlapTokens, s.TopK, s.MinimumSimilarity, s.UpdatedAt);
}
