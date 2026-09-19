namespace PlasticSurgery.Services;

/// <summary>
/// Turns text into an embedding vector. The single place embedding API calls happen — the
/// Knowledge Base services depend on this, never on an HTTP client directly. The provider/model come
/// from configuration (Embeddings:*), never hardcoded.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Length of every vector this service returns — must equal the dimension of the
    /// knowledge_chunks.embedding column (vector(1536) in Database/schema.sql).</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Embeds several texts (batched into as few provider calls as possible); the result
    /// is in the same order as the input.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
