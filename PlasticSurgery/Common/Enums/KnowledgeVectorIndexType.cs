namespace PlasticSurgery.Common.Enums;

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
