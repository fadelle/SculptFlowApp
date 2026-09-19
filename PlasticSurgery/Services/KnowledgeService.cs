using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class KnowledgeService : IKnowledgeService
{
    private const int MaxTitleLength = 200;
    private const int MaxCategoryLength = 50;

    private readonly ApplicationDbContext _db;
    private readonly IKnowledgeChunkingService _chunking;
    private readonly IEmbeddingService _embeddings;

    public KnowledgeService(ApplicationDbContext db, IKnowledgeChunkingService chunking, IEmbeddingService embeddings)
    {
        _db = db;
        _chunking = chunking;
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<KnowledgeDocumentResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var docs = await _db.KnowledgeDocuments
            .Where(d => d.ClinicId == clinicId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        var counts = await _db.KnowledgeChunks
            .Where(c => c.ClinicId == clinicId)
            .GroupBy(c => c.KnowledgeDocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count, ct);

        return docs.Select(d => ToResponse(d, counts.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<KnowledgeDocumentResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var doc = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == id, ct);
        return doc is null ? null : await ToResponseAsync(doc, ct);
    }

    public async Task<KnowledgeDocumentResponse> CreateAsync(Guid clinicId, SaveKnowledgeRequest request, CancellationToken ct = default)
    {
        var (title, category, content) = Validate(request);
        var (pieces, vectors) = await ChunkAndEmbedAsync(title, content, ct);

        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Title = title,
            Category = category,
            Content = content,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);
        await InsertChunksAsync(clinicId, doc.Id, pieces, vectors, ct);
        await tx.CommitAsync(ct);

        return ToResponse(doc, pieces.Count);
    }

    public async Task<KnowledgeDocumentResponse?> UpdateAsync(Guid clinicId, Guid id, SaveKnowledgeRequest request, CancellationToken ct = default)
    {
        var doc = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == id, ct);
        if (doc is null) return null;

        var (title, category, content) = Validate(request);
        var textChanged = title != doc.Title || content != doc.Content;

        IReadOnlyList<string> pieces = Array.Empty<string>();
        IReadOnlyList<float[]> vectors = Array.Empty<float[]>();
        if (textChanged)
        {
            (pieces, vectors) = await ChunkAndEmbedAsync(title, content, ct);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        doc.Title = title;
        doc.Category = category;
        doc.Content = content;
        doc.IsActive = request.IsActive;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (textChanged)
        {
            await _db.KnowledgeChunks.Where(c => c.KnowledgeDocumentId == doc.Id).ExecuteDeleteAsync(ct);
            await InsertChunksAsync(clinicId, doc.Id, pieces, vectors, ct);
        }
        await tx.CommitAsync(ct);

        return await ToResponseAsync(doc, ct);
    }

    public async Task<KnowledgeDocumentResponse?> SetActiveAsync(Guid clinicId, Guid id, bool isActive, CancellationToken ct = default)
    {
        var doc = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == id, ct);
        if (doc is null) return null;

        doc.IsActive = isActive;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await ToResponseAsync(doc, ct);
    }

    public async Task<bool> DeleteAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var doc = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ClinicId == clinicId && d.Id == id, ct);
        if (doc is null) return false;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.KnowledgeChunks.Where(c => c.KnowledgeDocumentId == doc.Id).ExecuteDeleteAsync(ct);
        _db.KnowledgeDocuments.Remove(doc);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<(IReadOnlyList<string> Pieces, IReadOnlyList<float[]> Vectors)> ChunkAndEmbedAsync(
        string title, string content, CancellationToken ct)
    {
        var pieces = _chunking.Chunk(content);
        if (pieces.Count == 0) throw new ArgumentException("Content is required.");

        // The title is part of what gets embedded so a chunk like "Yes, you stay overnight" still
        // carries which procedure/topic it's about; the stored (and returned) chunk text stays plain.
        var vectors = await _embeddings.EmbedBatchAsync(pieces.Select(p => $"{title}\n\n{p}").ToList(), ct);
        return (pieces, vectors);
    }

    private async Task InsertChunksAsync(
        Guid clinicId, Guid documentId, IReadOnlyList<string> pieces, IReadOnlyList<float[]> vectors, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
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

    private static (string Title, string Category, string Content) Validate(SaveKnowledgeRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        var content = (request.Content ?? string.Empty).Trim();
        var category = string.IsNullOrWhiteSpace(request.Category) ? KnowledgeCategory.General : request.Category.Trim().ToLowerInvariant();

        if (title.Length == 0) throw new ArgumentException("Title is required.");
        if (title.Length > MaxTitleLength) throw new ArgumentException($"Title must be {MaxTitleLength} characters or fewer.");
        if (category.Length > MaxCategoryLength) throw new ArgumentException($"Category must be {MaxCategoryLength} characters or fewer.");
        if (content.Length == 0) throw new ArgumentException("Content is required.");
        return (title, category, content);
    }

    private async Task<KnowledgeDocumentResponse> ToResponseAsync(KnowledgeDocument doc, CancellationToken ct) =>
        ToResponse(doc, await _db.KnowledgeChunks.CountAsync(c => c.KnowledgeDocumentId == doc.Id, ct));

    private static KnowledgeDocumentResponse ToResponse(KnowledgeDocument d, int chunkCount) => new(
        d.Id, d.ClinicId, d.Title, d.Category, d.Content, d.IsActive, chunkCount, d.CreatedAt, d.UpdatedAt);
}
