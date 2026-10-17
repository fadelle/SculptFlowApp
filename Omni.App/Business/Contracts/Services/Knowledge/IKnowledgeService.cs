using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Business.Contracts.Services.Knowledge;

/// <summary>
/// Client Knowledge Base CRUD for the dashboard. Every method is scoped by clientId (resolved from
/// the logged-in user via CurrentClientContext by the callers). Creating or editing a document's
/// title/content re-chunks and re-embeds it; the chunk swap is transactional, and embeddings are
/// computed BEFORE the transaction opens, so a failed embedding call never leaves a document with no
/// (or half-replaced) chunks.
/// </summary>
public interface IKnowledgeService
{
    Task<IReadOnlyList<KnowledgeDocumentResponse>> ListAsync(Guid clientId, CancellationToken ct = default);

    Task<KnowledgeDocumentResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default);

    /// <summary>Throws ArgumentException for invalid input, InvalidOperationException if embedding fails.</summary>
    Task<KnowledgeDocumentResponse> CreateAsync(Guid clientId, SaveKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>Maximum accepted upload size in bytes (config Knowledge:MaxUploadBytes, default 5 MB).</summary>
    long MaxUploadBytes { get; }

    /// <summary>Creates a document from ONE uploaded PDF/DOCX/TXT: validates size/type, extracts and
    /// normalizes the text, then runs the same chunk → embed → store pipeline as a manual entry. The
    /// original file is not kept — only its name/type/size and the extracted text (in Content).
    /// Throws InvalidDocumentException (an ArgumentException) for anything members can fix, and
    /// InvalidOperationException if embedding fails (nothing is saved in either case).</summary>
    Task<KnowledgeDocumentResponse> CreateFromUploadAsync(
        Guid clientId, UploadKnowledgeRequest request, string fileName, Stream content, long length, CancellationToken ct = default);

    /// <summary>Creates a document on behalf of an external ingestion subsystem (the website crawler): the
    /// text is already extracted and clean; this just runs it through the standard chunk → embed → store
    /// pipeline with the client's settings. Throws ArgumentException / InvalidOperationException like CreateAsync.</summary>
    Task<KnowledgeDocumentResponse> CreateFromSourceAsync(Guid clientId, ExternalKnowledgeDocument document, CancellationToken ct = default);

    /// <summary>Replaces the text (and title) of an externally-sourced document and rebuilds ITS chunks and
    /// embeddings only — active state and category are untouched. Null if it isn't in this client. Embeddings are
    /// computed before the swap, so a provider failure leaves the previous chunks in place.</summary>
    Task<KnowledgeDocumentResponse?> ReplaceSourceContentAsync(Guid clientId, Guid id, string title, string content, CancellationToken ct = default);

    /// <summary>Returns null if the document isn't in this client. For an uploaded document the text is
    /// kept as extracted (only title/category/active change). Always regenerates the chunks and
    /// embeddings using the client's current Knowledge Base settings (so re-saving an entry applies
    /// changed chunk size/overlap to it).</summary>
    Task<KnowledgeDocumentResponse?> UpdateAsync(Guid clientId, Guid id, SaveKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>Activate/deactivate without re-embedding — inactive documents keep their chunks but are
    /// excluded from AI search.</summary>
    Task<KnowledgeDocumentResponse?> SetActiveAsync(Guid clientId, Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>Deletes the document and its chunks. Returns false if it isn't in this client.</summary>
    Task<bool> DeleteAsync(Guid clientId, Guid id, CancellationToken ct = default);
}
