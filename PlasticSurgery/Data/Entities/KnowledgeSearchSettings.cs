namespace PlasticSurgery.Data.Entities;

/// <summary>
/// One row per clinic (unique on ClinicId) holding the Knowledge Base's retrieval/embedding
/// configuration. Created lazily with the system defaults the first time a clinic uses the Knowledge
/// Base (see IKnowledgeSettingsService). Only the tuning fields — chunk size, chunk overlap, top K and
/// minimum similarity — are editable by clinic staff; the embedding model, vector dimension,
/// similarity method and vector index type are persisted so it's always clear what a clinic's stored
/// vectors were built with, but are read-only (changing them means re-embedding and/or a database
/// migration). API keys and other secrets are NEVER stored here — they stay in configuration.
/// </summary>
public class KnowledgeSearchSettings
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    // Read-only in the UI — see class remarks.
    public string EmbeddingModel { get; set; } = string.Empty;
    public int VectorDimension { get; set; }
    public string SimilarityMethod { get; set; } = KnowledgeSimilarityMethod.Cosine;
    public string VectorIndexType { get; set; } = KnowledgeVectorIndexType.None;

    // Editable tuning fields.
    /// <summary>Approximate tokens per chunk (chunking measures ~4 characters per token — no tokenizer).</summary>
    public int ChunkSizeTokens { get; set; }
    public int ChunkOverlapTokens { get; set; }
    /// <summary>The most chunks a search returns for this clinic (the AI's optional limit can only lower it).</summary>
    public int TopK { get; set; }
    /// <summary>Cosine similarity floor (0–1); weaker matches are dropped so unrelated queries return nothing.</summary>
    public double MinimumSimilarity { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
}

public static class KnowledgeSimilarityMethod
{
    public const string Cosine = "cosine";

    public static string Label(string? value) =>
        string.Equals(value, Cosine, StringComparison.OrdinalIgnoreCase) ? "Cosine" : (value ?? "—");
}

public static class KnowledgeVectorIndexType
{
    /// <summary>No ANN index: each search is already narrowed to one clinic by clinic_id, so an exact
    /// scan is fast and has perfect recall (see Database/schema.sql).</summary>
    public const string None = "none";
    public const string Hnsw = "hnsw";
    public const string Ivfflat = "ivfflat";

    public static string Label(string? value) => value?.ToLowerInvariant() switch
    {
        None => "None — exact scan",
        Hnsw => "HNSW",
        Ivfflat => "IVFFlat",
        _ => value ?? "—"
    };
}
