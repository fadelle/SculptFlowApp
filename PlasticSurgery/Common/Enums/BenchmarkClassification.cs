namespace PlasticSurgery.Common.Enums;

public static class BenchmarkClassification
{
    public const string ExactChunkHit = "EXACT_CHUNK_HIT";
    public const string DocumentOnlyHit = "DOCUMENT_ONLY_HIT";
    public const string Miss = "MISS";
    /// <summary>The expected chunk no longer exists / changed / its document is inactive — not scored.</summary>
    public const string StaleCase = "STALE_CASE";
    /// <summary>The retrieval call itself failed (e.g. the embedding provider) — not scored, not a retrieval miss.</summary>
    public const string Error = "ERROR";
}
