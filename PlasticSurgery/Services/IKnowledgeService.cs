using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Clinic Knowledge Base CRUD for the dashboard. Every method is scoped by clinicId (resolved from
/// the logged-in user via CurrentClinicContext by the callers). Creating or editing a document's
/// title/content re-chunks and re-embeds it; the chunk swap is transactional, and embeddings are
/// computed BEFORE the transaction opens, so a failed embedding call never leaves a document with no
/// (or half-replaced) chunks.
/// </summary>
public interface IKnowledgeService
{
    Task<IReadOnlyList<KnowledgeDocumentResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);

    Task<KnowledgeDocumentResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    /// <summary>Throws ArgumentException for invalid input, InvalidOperationException if embedding fails.</summary>
    Task<KnowledgeDocumentResponse> CreateAsync(Guid clinicId, SaveKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>Maximum accepted upload size in bytes (config Knowledge:MaxUploadBytes, default 5 MB).</summary>
    long MaxUploadBytes { get; }

    /// <summary>Creates a document from ONE uploaded PDF/DOCX/TXT: validates size/type, extracts and
    /// normalizes the text, then runs the same chunk → embed → store pipeline as a manual entry. The
    /// original file is not kept — only its name/type/size and the extracted text (in Content).
    /// Throws InvalidDocumentException (an ArgumentException) for anything staff can fix, and
    /// InvalidOperationException if embedding fails (nothing is saved in either case).</summary>
    Task<KnowledgeDocumentResponse> CreateFromUploadAsync(
        Guid clinicId, UploadKnowledgeRequest request, string fileName, Stream content, long length, CancellationToken ct = default);

    /// <summary>Creates a document on behalf of an external ingestion subsystem (the website crawler): the
    /// text is already extracted and clean; this just runs it through the standard chunk → embed → store
    /// pipeline with the clinic's settings. Throws ArgumentException / InvalidOperationException like CreateAsync.</summary>
    Task<KnowledgeDocumentResponse> CreateFromSourceAsync(Guid clinicId, ExternalKnowledgeDocument document, CancellationToken ct = default);

    /// <summary>Replaces the text (and title) of an externally-sourced document and rebuilds ITS chunks and
    /// embeddings only — active state and category are untouched. Null if it isn't in this clinic. Embeddings are
    /// computed before the swap, so a provider failure leaves the previous chunks in place.</summary>
    Task<KnowledgeDocumentResponse?> ReplaceSourceContentAsync(Guid clinicId, Guid id, string title, string content, CancellationToken ct = default);

    /// <summary>Returns null if the document isn't in this clinic. For an uploaded document the text is
    /// kept as extracted (only title/category/active change). Always regenerates the chunks and
    /// embeddings using the clinic's current Knowledge Base settings (so re-saving an entry applies
    /// changed chunk size/overlap to it).</summary>
    Task<KnowledgeDocumentResponse?> UpdateAsync(Guid clinicId, Guid id, SaveKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>Activate/deactivate without re-embedding — inactive documents keep their chunks but are
    /// excluded from AI search.</summary>
    Task<KnowledgeDocumentResponse?> SetActiveAsync(Guid clinicId, Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>Deletes the document and its chunks. Returns false if it isn't in this clinic.</summary>
    Task<bool> DeleteAsync(Guid clinicId, Guid id, CancellationToken ct = default);
}
