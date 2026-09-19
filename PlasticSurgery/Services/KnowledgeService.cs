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
    private readonly IKnowledgeSettingsService _settings;
    private readonly IDocumentTextExtractor _extractor;

    public KnowledgeService(
        ApplicationDbContext db, IKnowledgeChunkingService chunking, IEmbeddingService embeddings,
        IKnowledgeSettingsService settings, IDocumentTextExtractor extractor, IConfiguration configuration)
    {
        _db = db;
        _chunking = chunking;
        _embeddings = embeddings;
        _settings = settings;
        _extractor = extractor;
        // Hard ceiling of 25 MB even if configured higher — Kestrel's own request limit is ~30 MB, and
        // the whole file is buffered in memory for parsing.
        MaxUploadBytes = Math.Clamp(configuration.GetValue("Knowledge:MaxUploadBytes", 5L * 1024 * 1024), 1024, 25L * 1024 * 1024);
    }

    public long MaxUploadBytes { get; }

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

    public Task<KnowledgeDocumentResponse> CreateAsync(Guid clinicId, SaveKnowledgeRequest request, CancellationToken ct = default) =>
        CreateCoreAsync(clinicId, request, upload: null, ct);

    public async Task<KnowledgeDocumentResponse> CreateFromUploadAsync(
        Guid clinicId, UploadKnowledgeRequest request, string fileName, Stream content, long length, CancellationToken ct = default)
    {
        fileName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (fileName.Length == 0 || length <= 0)
        {
            throw new InvalidDocumentException(length == 0 && fileName.Length > 0 ? "The file is empty." : "Choose a file to upload.");
        }
        if (length > MaxUploadBytes)
        {
            throw new InvalidDocumentException(
                $"This file is {length / 1024.0 / 1024.0:0.#} MB — the maximum upload size is {MaxUploadBytes / 1024.0 / 1024.0:0.#} MB.");
        }

        // Extraction validates the type (extension + contents) and throws a staff-readable message for
        // every user-fixable problem. Nothing is written until the text has been extracted successfully.
        var text = await _extractor.ExtractAsync(content, fileName, ct);

        // Title defaults to the file name (without extension) so a quick upload doesn't need one typed.
        var title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(fileName) : request.Title;
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];

        // From here it's exactly the manual-entry pipeline: same chunker, same settings, same embeddings,
        // same transactional chunk write — so the document is immediately searchable.
        return await CreateCoreAsync(
            clinicId, new SaveKnowledgeRequest(title, request.Category, text, request.IsActive),
            new UploadInfo(fileName.Length > 255 ? fileName[^255..] : fileName, DocumentTextExtractor.MimeTypeFor(fileName), length), ct);
    }

    private sealed record UploadInfo(string FileName, string MimeType, long SizeBytes);

    private async Task<KnowledgeDocumentResponse> CreateCoreAsync(
        Guid clinicId, SaveKnowledgeRequest request, UploadInfo? upload, CancellationToken ct)
    {
        var (title, category, content) = Validate(request);
        var (pieces, vectors) = await ChunkAndEmbedAsync(clinicId, title, content, ct);

        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Title = title,
            Category = category,
            Content = content,
            SourceType = upload is null ? KnowledgeSourceType.Manual : KnowledgeSourceType.Upload,
            OriginalFileName = upload?.FileName,
            MimeType = upload?.MimeType,
            FileSizeBytes = upload?.SizeBytes,
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

        // An uploaded document's text is what was extracted from the file — it isn't editable here
        // (re-upload to change it), whatever the caller sent. Title/category/active stay editable, and
        // the stored text is re-chunked below exactly like a manual entry.
        if (doc.SourceType == KnowledgeSourceType.Upload) request = request with { Content = doc.Content };

        var (title, category, content) = Validate(request);
        // Saving always re-chunks and re-embeds with the clinic's CURRENT settings — that's how a
        // changed chunk size/overlap is applied to an existing entry (just re-save it).
        var (pieces, vectors) = await ChunkAndEmbedAsync(clinicId, title, content, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        doc.Title = title;
        doc.Category = category;
        doc.Content = content;
        doc.IsActive = request.IsActive;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _db.KnowledgeChunks.Where(c => c.KnowledgeDocumentId == doc.Id).ExecuteDeleteAsync(ct);
        await InsertChunksAsync(clinicId, doc.Id, pieces, vectors, ct);
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
        Guid clinicId, string title, string content, CancellationToken ct)
    {
        // Chunk sizes and the embedding model/dimension come from the clinic's persisted settings.
        var settings = await _settings.GetAsync(clinicId, ct);

        var pieces = _chunking.Chunk(content, settings.ChunkSizeTokens, settings.ChunkOverlapTokens);
        if (pieces.Count == 0) throw new ArgumentException("Content is required.");

        // The title is part of what gets embedded so a chunk like "Yes, you stay overnight" still
        // carries which procedure/topic it's about; the stored (and returned) chunk text stays plain.
        var vectors = await _embeddings.EmbedBatchAsync(
            pieces.Select(p => $"{title}\n\n{p}").ToList(), settings.EmbeddingModel, settings.VectorDimension, ct);
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
        d.Id, d.ClinicId, d.Title, d.Category, d.Content, d.IsActive, chunkCount, d.CreatedAt, d.UpdatedAt,
        d.SourceType, d.OriginalFileName, d.MimeType, d.FileSizeBytes);
}
