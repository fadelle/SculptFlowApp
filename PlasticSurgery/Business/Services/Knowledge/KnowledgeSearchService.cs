using PlasticSurgery.Business.Contracts.HttpClients.OpenAi;
using PlasticSurgery.Business.Contracts.Services.Knowledge;
using PlasticSurgery.Entities.Dtos.Knowledge;
using PlasticSurgery.Entities.Responses.Knowledge;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Business.Services.Knowledge;

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

    public async Task<KnowledgeSearchResponse> SearchAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default)
    {
        var ranked = await SearchRankedAsync(clinicId, query, limit, ct);

        // Chunks scoring below the clinic's minimum similarity are dropped, so an unrelated query yields an
        // empty list rather than the least-bad match.
        var results = ranked
            .Where(r => r.MeetsMinimumSimilarity)
            .Select(r => new KnowledgeSearchResult(r.DocumentId, r.Title, r.Category, r.Content, Math.Round(r.Score, 4)))
            .ToList();

        return new KnowledgeSearchResponse(results);
    }

    public async Task<IReadOnlyList<RankedKnowledgeChunk>> SearchRankedAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default)
    {
        if (clinicId == Guid.Empty || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RankedKnowledgeChunk>();
        }

        // Top K, the similarity floor and the embedding model/dimension are the clinic's persisted
        // settings. Top K is the clinic's ceiling: the AI's optional limit can only lower it.
        var settings = await _settings.GetAsync(clinicId, ct);
        var take = Math.Clamp(limit ?? settings.TopK, 1, settings.TopK);

        var queryVector = await _embeddings.EmbedAsync(query.Trim(), settings.EmbeddingModel, settings.VectorDimension, ct);

        // Clinic isolation: the WHERE clause pins clinic_id on BOTH the chunk and its document, and
        // only active documents qualify — the similarity ranking only ever sees that slice.
        var rows = await _documents.SearchChunksAsync(clinicId, queryVector, take, ct);

        return rows
            .Select(r => new RankedKnowledgeChunk(
                r.ChunkId, r.DocumentId, r.Title, r.Category, r.Content, r.Score, r.Score >= settings.MinimumSimilarity))
            .ToList();
    }
}
