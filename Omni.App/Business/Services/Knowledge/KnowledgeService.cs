using Omni.App.Business.Contracts.Engines.Knowledge;
using Omni.App.Business.Contracts.HttpClients.OpenAi;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Engines.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Knowledge;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Services.Knowledge;

public class KnowledgeService : IKnowledgeService
{
    private const int MaxTitleLength = 200;
    private const int MaxCategoryLength = 50;

    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IKnowledgeChunkingService _chunking;
    private readonly IEmbeddingService _embeddings;
    private readonly IKnowledgeSettingsService _settings;
    private readonly IDocumentTextExtractor _extractor;
    private readonly IConfigManager _config;

    public KnowledgeService(
        IKnowledgeDocumentRepository documents, IUnitOfWork unitOfWork, IKnowledgeChunkingService chunking, IEmbeddingService embeddings,
        IKnowledgeSettingsService settings, IDocumentTextExtractor extractor, IConfigManager config)
    {
        _documents = documents;
        _unitOfWork = unitOfWork;
        _chunking = chunking;
        _embeddings = embeddings;
        _settings = settings;
        _extractor = extractor;
        // Hard ceiling of 25 MB even if configured higher — Kestrel's own request limit is ~30 MB, and
        // the whole file is buffered in memory for parsing.
        _config = config;
    }

    public long MaxUploadBytes => _config.KnowledgeMaxUploadBytes;

    public async Task<IReadOnlyList<KnowledgeDocumentResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var docs = await _documents.ListForClientAsync(clientId, ct);
        var counts = await _documents.CountChunksByDocumentAsync(clientId, ct);

        return docs.Select(d => ToResponse(d, counts.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<KnowledgeDocumentResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var doc = await _documents.GetAsync(clientId, id, ct);
        return doc is null ? null : await ToResponseAsync(doc, ct);
    }

    public Task<KnowledgeDocumentResponse> CreateAsync(Guid clientId, SaveKnowledgeRequest request, CancellationToken ct = default) =>
        CreateCoreAsync(clientId, request, source: null, ct);

    public async Task<KnowledgeDocumentResponse> CreateFromUploadAsync(
        Guid clientId, UploadKnowledgeRequest request, string fileName, Stream content, long length, CancellationToken ct = default)
    {
        fileName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (fileName.Length == 0 || length <= 0)
        {
            throw length == 0 && fileName.Length > 0
                ? new InvalidDocumentException("knowledge.documents.errors.empty")
                : new InvalidDocumentException("knowledge.documents.errors.fileRequired");
        }
        if (length > MaxUploadBytes)
        {
            throw new InvalidDocumentException("knowledge.documents.errors.tooLarge")
                .WithArgs($"{length / 1024.0 / 1024.0:0.#}", $"{MaxUploadBytes / 1024.0 / 1024.0:0.#}");
        }

        // Extraction validates the type (extension + contents) and throws a user-readable message for
        // every user-fixable problem. Nothing is written until the text has been extracted successfully.
        var text = await _extractor.ExtractAsync(content, fileName, ct);

        // Title defaults to the file name (without extension) so a quick upload doesn't need one typed.
        var title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(fileName) : request.Title;
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];

        // From here it's exactly the manual-entry pipeline: same chunker, same settings, same embeddings,
        // same transactional chunk write — so the document is immediately searchable.
        return await CreateCoreAsync(
            clientId, new SaveKnowledgeRequest(title, request.Category, text, request.IsActive),
            new SourceInfo(KnowledgeSourceType.Upload, fileName.Length > 255 ? fileName[^255..] : fileName, DocumentTextExtractor.MimeTypeFor(fileName), length), ct);
    }

    public Task<KnowledgeDocumentResponse> CreateFromSourceAsync(Guid clientId, ExternalKnowledgeDocument document, CancellationToken ct = default) =>
        // Exactly the manual-entry pipeline (same chunker, client settings, embeddings, transactional chunk write);
        // only the provenance columns differ. The caller (an ingestion subsystem) has already produced clean text.
        CreateCoreAsync(
            clientId, new SaveKnowledgeRequest(document.Title, document.Category, document.Content, document.IsActive),
            new SourceInfo(document.SourceType, SourceUrl: document.SourceUrl), ct);

    public async Task<KnowledgeDocumentResponse?> ReplaceSourceContentAsync(
        Guid clientId, Guid id, string title, string content, CancellationToken ct = default)
    {
        var doc = await _documents.GetAsync(clientId, id, ct);
        if (doc is null) return null;

        var (cleanTitle, _, cleanContent) = Validate(new SaveKnowledgeRequest(title, doc.Category, content, doc.IsActive));
        // Embeddings are computed BEFORE the transaction, so a provider failure leaves the old chunks intact.
        var (pieces, vectors) = await ChunkAndEmbedAsync(clientId, cleanTitle, cleanContent, ct);

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        doc.Title = cleanTitle;
        doc.Content = cleanContent;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        await _documents.DeleteChunksAsync(doc.Id, ct);
        await InsertChunksAsync(clientId, doc.Id, pieces, vectors, ct);
        await tx.CommitAsync(ct);

        return ToResponse(doc, pieces.Count);
    }

    private sealed record SourceInfo(string SourceType, string? FileName = null, string? MimeType = null, long? SizeBytes = null, string? SourceUrl = null);

    private async Task<KnowledgeDocumentResponse> CreateCoreAsync(
        Guid clientId, SaveKnowledgeRequest request, SourceInfo? source, CancellationToken ct)
    {
        var (title, category, content) = Validate(request);
        var (pieces, vectors) = await ChunkAndEmbedAsync(clientId, title, content, ct);

        var now = DateTimeOffset.UtcNow;
        var doc = new KnowledgeDocument
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Title = title,
            Category = category,
            Content = content,
            SourceType = source?.SourceType ?? KnowledgeSourceType.Manual,
            OriginalFileName = source?.FileName,
            MimeType = source?.MimeType,
            FileSizeBytes = source?.SizeBytes,
            SourceUrl = source?.SourceUrl,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        _documents.Add(doc);
        await _unitOfWork.SaveChangesAsync(ct);
        await InsertChunksAsync(clientId, doc.Id, pieces, vectors, ct);
        await tx.CommitAsync(ct);

        return ToResponse(doc, pieces.Count);
    }

    public async Task<KnowledgeDocumentResponse?> UpdateAsync(Guid clientId, Guid id, SaveKnowledgeRequest request, CancellationToken ct = default)
    {
        var doc = await _documents.GetAsync(clientId, id, ct);
        if (doc is null) return null;

        // An uploaded document's text is what was extracted from the file — it isn't editable here
        // (re-upload to change it), whatever the caller sent. Title/category/active stay editable, and
        // the stored text is re-chunked below exactly like a manual entry.
        if (doc.SourceType != KnowledgeSourceType.Manual) request = request with { Content = doc.Content };

        var (title, category, content) = Validate(request);
        // Saving always re-chunks and re-embeds with the client's CURRENT settings — that's how a
        // changed chunk size/overlap is applied to an existing entry (just re-save it).
        var (pieces, vectors) = await ChunkAndEmbedAsync(clientId, title, content, ct);

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        doc.Title = title;
        doc.Category = category;
        doc.Content = content;
        doc.IsActive = request.IsActive;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        await _documents.DeleteChunksAsync(doc.Id, ct);
        await InsertChunksAsync(clientId, doc.Id, pieces, vectors, ct);
        await tx.CommitAsync(ct);

        return await ToResponseAsync(doc, ct);
    }

    public async Task<KnowledgeDocumentResponse?> SetActiveAsync(Guid clientId, Guid id, bool isActive, CancellationToken ct = default)
    {
        var doc = await _documents.GetAsync(clientId, id, ct);
        if (doc is null) return null;

        doc.IsActive = isActive;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(doc, ct);
    }

    public async Task<bool> DeleteAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var doc = await _documents.GetAsync(clientId, id, ct);
        if (doc is null) return false;

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        await _documents.DeleteChunksAsync(doc.Id, ct);
        _documents.Remove(doc);
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<(IReadOnlyList<string> Pieces, IReadOnlyList<float[]> Vectors)> ChunkAndEmbedAsync(
        Guid clientId, string title, string content, CancellationToken ct)
    {
        // Chunk sizes and the embedding model/dimension come from the client's persisted settings.
        var settings = await _settings.GetAsync(clientId, ct);

        var pieces = _chunking.Chunk(content, settings.ChunkSizeTokens, settings.ChunkOverlapTokens);
        if (pieces.Count == 0) throw new ArgumentException("knowledge.errors.contentRequired");

        // The title is part of what gets embedded so a chunk like "Yes, you stay overnight" still
        // carries which service/topic it's about; the stored (and returned) chunk text stays plain.
        var vectors = await _embeddings.EmbedBatchAsync(
            pieces.Select(p => $"{title}\n\n{p}").ToList(), settings.EmbeddingModel, settings.VectorDimension, ct);
        return (pieces, vectors);
    }

    private Task InsertChunksAsync(
        Guid clientId, Guid documentId, IReadOnlyList<string> pieces, IReadOnlyList<float[]> vectors, CancellationToken ct) =>
        _documents.InsertChunksAsync(clientId, documentId, pieces, vectors, DateTimeOffset.UtcNow, ct);

    private static (string Title, string Category, string Content) Validate(SaveKnowledgeRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        var content = (request.Content ?? string.Empty).Trim();
        var category = string.IsNullOrWhiteSpace(request.Category) ? KnowledgeCategory.General : request.Category.Trim().ToLowerInvariant();

        if (title.Length == 0) throw new ArgumentException("knowledge.errors.titleRequired");
        if (title.Length > MaxTitleLength) throw new ArgumentException("knowledge.errors.titleTooLong").WithArgs(MaxTitleLength);
        if (category.Length > MaxCategoryLength) throw new ArgumentException("knowledge.errors.categoryTooLong").WithArgs(MaxCategoryLength);
        if (content.Length == 0) throw new ArgumentException("knowledge.errors.contentRequired");
        return (title, category, content);
    }

    private async Task<KnowledgeDocumentResponse> ToResponseAsync(KnowledgeDocument doc, CancellationToken ct) =>
        ToResponse(doc, await _documents.CountChunksAsync(doc.Id, ct));

    private static KnowledgeDocumentResponse ToResponse(KnowledgeDocument d, int chunkCount) => new(
        d.Id, d.ClientId, d.Title, d.Category, d.Content, d.IsActive, chunkCount, d.CreatedAt, d.UpdatedAt,
        d.SourceType, d.OriginalFileName, d.MimeType, d.FileSizeBytes, d.SourceUrl);
}
