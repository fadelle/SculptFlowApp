namespace PlasticSurgery.Common.Enums;

public static class BenchmarkStaleReason
{
    public const string ChunkMissing = "chunk_missing";
    public const string ChunkChanged = "chunk_changed";
    public const string DocumentInactive = "document_inactive";

    public static string Label(string? reason) => reason switch
    {
        ChunkMissing => "The chunk no longer exists (the document was re-saved, re-chunked or deleted).",
        ChunkChanged => "The chunk's text changed since this case was created.",
        DocumentInactive => "The chunk's document is inactive, so search never returns it.",
        _ => string.Empty
    };
}
