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

    /// <summary>Returns null if the document isn't in this clinic. Only regenerates chunks/embeddings
    /// when the title or content actually changed.</summary>
    Task<KnowledgeDocumentResponse?> UpdateAsync(Guid clinicId, Guid id, SaveKnowledgeRequest request, CancellationToken ct = default);

    /// <summary>Activate/deactivate without re-embedding — inactive documents keep their chunks but are
    /// excluded from AI search.</summary>
    Task<KnowledgeDocumentResponse?> SetActiveAsync(Guid clinicId, Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>Deletes the document and its chunks. Returns false if it isn't in this clinic.</summary>
    Task<bool> DeleteAsync(Guid clinicId, Guid id, CancellationToken ct = default);
}
