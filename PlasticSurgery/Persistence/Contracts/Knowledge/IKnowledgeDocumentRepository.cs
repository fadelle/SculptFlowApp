using PlasticSurgery.Entities.Dtos.Knowledge;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Knowledge;

/// <summary>Knowledge Base documents and their embedded chunks.</summary>
public interface IKnowledgeDocumentRepository
{
    /// <summary>Tracked, newest change first.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<KnowledgeDocument?> GetAsync(Guid clinicId, Guid documentId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clinicId, Guid documentId, CancellationToken ct = default);

    void Add(KnowledgeDocument document);

    void Remove(KnowledgeDocument document);

    /// <summary>Chunk count per document for the clinic.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountChunksByDocumentAsync(Guid clinicId, CancellationToken ct = default);

    Task<int> CountChunksAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Deletes the document's chunks directly in the database.</summary>
    Task DeleteChunksAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Writes the chunks with their embeddings (pgvector), in order, directly in the database.</summary>
    Task InsertChunksAsync(Guid clinicId, Guid documentId, IReadOnlyList<string> pieces, IReadOnlyList<float[]> vectors,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// The clinic's active-document chunks closest to <paramref name="queryVector"/> (cosine), best first. Clinic
    /// isolation is pinned on both the chunk and its document.
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunkMatch>> SearchChunksAsync(Guid clinicId, float[] queryVector, int take, CancellationToken ct = default);
}
