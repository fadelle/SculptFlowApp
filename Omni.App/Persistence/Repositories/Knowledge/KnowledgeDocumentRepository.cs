using Microsoft.EntityFrameworkCore;
using Npgsql;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Knowledge;

namespace Omni.App.Persistence.Repositories.Knowledge;

public class KnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly ApplicationDbContext _db;

    public KnowledgeDocumentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListForClientAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.KnowledgeDocuments
            .Where(d => d.ClientId == clientId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

    public Task<KnowledgeDocument?> GetAsync(Guid clientId, Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ClientId == clientId && d.Id == documentId, ct);

    public Task<bool> ExistsAsync(Guid clientId, Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeDocuments.AnyAsync(d => d.Id == documentId && d.ClientId == clientId, ct);

    public void Add(KnowledgeDocument document) => _db.KnowledgeDocuments.Add(document);

    public void Remove(KnowledgeDocument document) => _db.KnowledgeDocuments.Remove(document);

    public async Task<IReadOnlyDictionary<Guid, int>> CountChunksByDocumentAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.KnowledgeChunks
            .Where(c => c.ClientId == clientId)
            .GroupBy(c => c.KnowledgeDocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, ct);

    public Task<int> CountChunksAsync(Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeChunks.CountAsync(c => c.KnowledgeDocumentId == documentId, ct);

    public Task DeleteChunksAsync(Guid documentId, CancellationToken ct = default) =>
        _db.KnowledgeChunks.Where(c => c.KnowledgeDocumentId == documentId).ExecuteDeleteAsync(ct);

    /// <summary>Rows per insert statement: one round trip per batch instead of one per chunk, while keeping each
    /// statement's parameters (about 15 KB of vector text per chunk) to a few MB.</summary>
    private const int ChunkInsertBatchSize = 200;

    public async Task InsertChunksAsync(Guid clientId, Guid documentId, IReadOnlyList<string> pieces, IReadOnlyList<float[]> vectors,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (pieces.Count != vectors.Count) throw new ArgumentException("Every chunk needs exactly one embedding.");

        for (var start = 0; start < pieces.Count; start += ChunkInsertBatchSize)
        {
            var count = Math.Min(ChunkInsertBatchSize, pieces.Count - start);
            var ids = new Guid[count];
            var indexes = new int[count];
            var contents = new string[count];
            var embeddings = new string[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = Guid.NewGuid();
                indexes[i] = start + i;
                contents[i] = pieces[start + i];
                embeddings[i] = VectorLiteral.From(vectors[start + i]);
            }

            // One statement per batch: the arrays are unnested into rows (same order, same values as one insert per chunk).
            await _db.Database.ExecuteSqlRawAsync(
                @"insert into knowledge.knowledge_chunks (id, client_id, knowledge_document_id, chunk_index, content, embedding, created_at, updated_at)
                  select t.id, @client, @doc, t.idx, t.content, t.embedding::vector, @now, @now
                  from unnest(@ids, @idxs, @contents, @embeddings) as t(id, idx, content, embedding)",
                new object[]
                {
                    new NpgsqlParameter("ids", ids),
                    new NpgsqlParameter("idxs", indexes),
                    new NpgsqlParameter("contents", contents),
                    new NpgsqlParameter("embeddings", embeddings),
                    new NpgsqlParameter("client", clientId),
                    new NpgsqlParameter("doc", documentId),
                    new NpgsqlParameter("now", now)
                },
                ct);
        }
    }

    public Task<IReadOnlyList<KnowledgeChunkMatch>> SearchChunksAsync(Guid clientId, float[] queryVector, int take,
        CancellationToken ct = default) => SearchChunksAsync(clientId, queryVector, take, null, ct);

    public async Task<IReadOnlyList<KnowledgeChunkMatch>> SearchChunksAsync(Guid clientId, float[] queryVector, int take,
        KnowledgeSourceFilter? sources, CancellationToken ct = default)
    {
        // A bot limited to some sources sees only those documents and the documents its website sources' pages produced.
        var rows = await _db.Database.SqlQueryRaw<SearchRow>(
            @"select c.id as ""ChunkId"", d.id as ""DocumentId"", d.title as ""Title"", d.category as ""Category"", c.content as ""Content"",
                     (1 - (c.embedding <=> @q::vector))::double precision as ""Score""
              from knowledge.knowledge_chunks c
              join knowledge.knowledge_documents d on d.id = c.knowledge_document_id
              where c.client_id = @client and d.client_id = @client and d.is_active = true
                and (@all or d.id = any(@docs) or exists (
                      select 1 from knowledge.knowledge_website_pages p
                      where p.knowledge_document_id = d.id and p.client_id = @client and p.website_source_id = any(@sites)))
              order by c.embedding <=> @q::vector
              limit @lim",
            new NpgsqlParameter("q", VectorLiteral.From(queryVector)),
            new NpgsqlParameter("client", clientId),
            new NpgsqlParameter("all", sources is null),
            new NpgsqlParameter("docs", (sources?.DocumentIds ?? []).ToArray()),
            new NpgsqlParameter("sites", (sources?.WebsiteSourceIds ?? []).ToArray()),
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
