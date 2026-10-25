using Omni.App.Business.Contracts.HttpClients.OpenAi;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Persistence.Contracts.Knowledge;

namespace Omni.App.Business.Services.Knowledge;

public class KnowledgeSearchService : IKnowledgeSearchService
{
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IEmbeddingService _embeddings;
    private readonly IKnowledgeSettingsService _settings;

    public KnowledgeSearchService(IKnowledgeDocumentRepository documents, IEmbeddingService embeddings, IKnowledgeSettingsService settings)
    {
        _documents = documents;
        _embeddings = embeddings;
        _settings = settings;
    }

    public Task<KnowledgeSearchResponse> SearchAsync(Guid clientId, string query, int? limit = null, CancellationToken ct = default) =>
        SearchAsync(clientId, query, limit, null, ct);

    public async Task<KnowledgeSearchResponse> SearchAsync(Guid clientId, string query, int? limit, KnowledgeSourceFilter? sources,
        CancellationToken ct = default)
    {
        var ranked = await RankAsync(clientId, query, limit, sources, ct);

        // Chunks scoring below the client's minimum similarity are dropped, so an unrelated query yields an
        // empty list rather than the least-bad match.
        var results = ranked
            .Where(r => r.MeetsMinimumSimilarity)
            .Select(r => new KnowledgeSearchResult(r.DocumentId, r.Title, r.Category, r.Content, Math.Round(r.Score, 4)))
            .ToList();

        return new KnowledgeSearchResponse(results);
    }

    public Task<IReadOnlyList<RankedKnowledgeChunk>> SearchRankedAsync(Guid clientId, string query, int? limit = null, CancellationToken ct = default) =>
        RankAsync(clientId, query, limit, null, ct);

    private async Task<IReadOnlyList<RankedKnowledgeChunk>> RankAsync(Guid clientId, string query, int? limit, KnowledgeSourceFilter? sources,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RankedKnowledgeChunk>();
        }

        // Top K, the similarity floor and the embedding model/dimension are the client's persisted
        // settings. Top K is the client's ceiling: the AI's optional limit can only lower it.
        var settings = await _settings.GetAsync(clientId, ct);
        var take = Math.Clamp(limit ?? settings.TopK, 1, settings.TopK);

        var queryVector = await _embeddings.EmbedAsync(query.Trim(), settings.EmbeddingModel, settings.VectorDimension, ct);

        // Client isolation: the WHERE clause pins client_id on BOTH the chunk and its document, and
        // only active documents qualify — the similarity ranking only ever sees that slice.
        var rows = sources is null
            ? await _documents.SearchChunksAsync(clientId, queryVector, take, ct)
            : await _documents.SearchChunksAsync(clientId, queryVector, take, sources, ct);

        return rows
            .Select(r => new RankedKnowledgeChunk(
                r.ChunkId, r.DocumentId, r.Title, r.Category, r.Content, r.Score, r.Score >= settings.MinimumSimilarity))
            .ToList();
    }
}
