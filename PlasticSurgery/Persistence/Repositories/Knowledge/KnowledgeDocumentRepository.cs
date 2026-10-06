using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Knowledge;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Knowledge;

namespace PlasticSurgery.Persistence.Repositories.Knowledge;

public class KnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly ApplicationDbContext _db;

    public KnowledgeDocumentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeDocuments
            .Where(d => d.ClinicId == clinicId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

    public Task<KnowledgeDocument?> GetAsync(Guid clinicId, Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == documentId, ct);

    public Task<bool> ExistsAsync(Guid clinicId, Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeDocuments.AnyAsync(d => d.Id == documentId && d.ClinicId == clinicId, ct);

    public void Add(KnowledgeDocument document) => _db.KnowledgeDocuments.Add(document);

    public void Remove(KnowledgeDocument document) => _db.KnowledgeDocuments.Remove(document);

    public async Task<IReadOnlyDictionary<Guid, int>> CountChunksByDocumentAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.KnowledgeChunks
            .Where(c => c.ClinicId == clinicId)
            .GroupBy(c => c.KnowledgeDocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, ct);

    public Task<int> CountChunksAsync(Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeChunks.CountAsync(c => c.KnowledgeDocumentId == documentId, ct);

    public Task DeleteChunksAsync(Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeChunks.Where(c => c.KnowledgeDocumentId == documentId).ExecuteDeleteAsync(ct);

    public async Task InsertChunksAsync(Guid clinicId, Guid documentId, IReadOnlyList<string> pieces, IReadOnlyList<float[]> vectors,
        DateTimeOffset now, CancellationToken ct = default)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            await _db.Database.ExecuteSqlRawAsync(
                @"insert into knowledge_chunks (id, clinic_id, knowledge_document_id, chunk_index, content, embedding, created_at, updated_at)
                  values (@id, @clinic, @doc, @idx, @content, @embedding::vector, @now, @now)",
                new object[]
                {
                    new NpgsqlParameter("id", Guid.NewGuid()),
                    new NpgsqlParameter("clinic", clinicId),
                    new NpgsqlParameter("doc", documentId),
                    new NpgsqlParameter("idx", i),
                    new NpgsqlParameter("content", pieces[i]),
                    new NpgsqlParameter("embedding", VectorLiteral.From(vectors[i])),
                    new NpgsqlParameter("now", now)
                },
                ct);
        }
    }

    public async Task<IReadOnlyList<KnowledgeChunkMatch>> SearchChunksAsync(Guid clinicId, float[] queryVector, int take,
        CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQueryRaw<SearchRow>(
            @"select c.id as ""ChunkId"", d.id as ""DocumentId"", d.title as ""Title"", d.category as ""Category"", c.content as ""Content"",
                     (1 - (c.embedding <=> @q::vector))::double precision as ""Score""
              from knowledge_chunks c
              join knowledge_documents d on d.id = c.knowledge_document_id
              where c.clinic_id = @clinic and d.clinic_id = @clinic and d.is_active = true
              order by c.embedding <=> @q::vector
              limit @lim",
            new NpgsqlParameter("q", VectorLiteral.From(queryVector)),
            new NpgsqlParameter("clinic", clinicId),
            new NpgsqlParameter("lim", take))
            .ToListAsync(ct);

        return rows.Select(r => new KnowledgeChunkMatch(r.ChunkId, r.DocumentId, r.Title, r.Category, r.Content, r.Score)).ToList();
    }

    private class SearchRow
    {
        public Guid ChunkId { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
