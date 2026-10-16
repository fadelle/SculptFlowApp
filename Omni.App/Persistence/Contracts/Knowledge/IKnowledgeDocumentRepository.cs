using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Knowledge;

/// <summary>Knowledge Base documents and their embedded chunks.</summary>
public interface IKnowledgeDocumentRepository
{
    /// <summary>Tracked, newest change first.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<KnowledgeDocument?> GetAsync(Guid clientId, Guid documentId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clientId, Guid documentId, CancellationToken ct = default);

    void Add(KnowledgeDocument document);

    void Remove(KnowledgeDocument document);

    /// <summary>Chunk count per document for the client.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountChunksByDocumentAsync(Guid clientId, CancellationToken ct = default);

    Task<int> CountChunksAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Deletes the document's chunks directly in the database.</summary>
    Task DeleteChunksAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Writes the chunks with their embeddings (pgvector), in order, directly in the database.</summary>
    Task InsertChunksAsync(Guid clientId, Guid documentId, IReadOnlyList<string> pieces, IReadOnlyList<float[]> vectors,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// The client's active-document chunks closest to <paramref name="queryVector"/> (cosine), best first. Client
    /// isolation is pinned on both the chunk and its document.
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunkMatch>> SearchChunksAsync(Guid clientId, float[] queryVector, int take, CancellationToken ct = default);
}
