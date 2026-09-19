using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Data;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class KnowledgeSearchService : IKnowledgeSearchService
{
    private const int DefaultLimit = 5;
    private const int MaxLimit = 10;
    private const double DefaultMinScore = 0.30;

    private readonly ApplicationDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly IConfiguration _configuration;

    public KnowledgeSearchService(ApplicationDbContext db, IEmbeddingService embeddings, IConfiguration configuration)
    {
        _db = db;
        _embeddings = embeddings;
        _configuration = configuration;
    }

    public async Task<KnowledgeSearchResponse> SearchAsync(Guid clinicId, string query, int? limit = null, CancellationToken ct = default)
    {
        if (clinicId == Guid.Empty || string.IsNullOrWhiteSpace(query))
        {
            return new KnowledgeSearchResponse(Array.Empty<KnowledgeSearchResult>());
        }

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var minScore = double.TryParse(_configuration["Knowledge:MinScore"], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : DefaultMinScore;

        var queryVector = VectorLiteral.From(await _embeddings.EmbedAsync(query.Trim(), ct));

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
            .Where(r => r.Score >= minScore)
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
