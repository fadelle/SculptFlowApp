using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Data;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class KnowledgeSearchService : IKnowledgeSearchService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly IKnowledgeSettingsService _settings;

    public KnowledgeSearchService(ApplicationDbContext db, IEmbeddingService embeddings, IKnowledgeSettingsService settings)
    {
        _db = db;
        _embeddings = embeddings;
        _settings = settings;
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default)
    {
        if (clinicId == Guid.Empty || string.IsNullOrWhiteSpace(query))
        {
            return new KnowledgeSearchResponse(Array.Empty<KnowledgeSearchResult>());
        }

        // Top K, the similarity floor and the embedding model/dimension are the clinic's persisted
        // settings. Top K is the clinic's ceiling: the AI's optional limit can only lower it.
        var settings = await _settings.GetAsync(clinicId, ct);
        var take = Math.Clamp(limit ?? settings.TopK, 1, settings.TopK);

        var queryVector = VectorLiteral.From(
            await _embeddings.EmbedAsync(query.Trim(), settings.EmbeddingModel, settings.VectorDimension, ct));

        // Clinic isolation: the WHERE clause pins clinic_id on BOTH the chunk and its document, and
        // only active documents qualify — the similarity ranking only ever sees that slice.
        var rows = await _db.Database.SqlQueryRaw<SearchRow>(
            @"select d.id as ""DocumentId"", d.title as ""Title"", d.category as ""Category"", c.content as ""Content"",
                     (1 - (c.embedding <=> @q::vector))::double precision as ""Score""
              from knowledge_chunks c
              join knowledge_documents d on d.id = c.knowledge_document_id
              where c.clinic_id = @clinic and d.clinic_id = @clinic and d.is_active = true
              order by c.embedding <=> @q::vector
              limit @lim",
            new NpgsqlParameter("q", queryVector),
            new NpgsqlParameter("clinic", clinicId),
            new NpgsqlParameter("lim", take))
            .ToListAsync(ct);

        var results = rows
            .Where(r => r.Score >= settings.MinimumSimilarity)
            .Select(r => new KnowledgeSearchResult(r.DocumentId, r.Title, r.Category, r.Content, Math.Round(r.Score, 4)))
            .ToList();

        return new KnowledgeSearchResponse(results);
    }

    private class SearchRow
    {
        public Guid DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
