using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

// Knowledge Retrieval Benchmark (see Integrations/Knowledge/Benchmark). These entities are a standalone
// diagnostic feature: nothing in production ingestion or search reads or writes them, and no benchmark state
// is stored on KnowledgeDocument / KnowledgeChunk.

/// <summary>One realistic patient question plus the single chunk that is known to answer it. The expected
/// document/chunk ids are plain columns (not foreign keys) on purpose: chunks get new ids whenever their
/// document is re-saved, and the benchmark must never block production ingestion. A case whose chunk has
/// disappeared or changed is flagged <see cref="IsStale"/> and excluded from scoring.</summary>
public class KnowledgeRetrievalBenchmarkCase
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    public string Question { get; set; } = string.Empty;

    public Guid ExpectedDocumentId { get; set; }
    public Guid ExpectedChunkId { get; set; }

    /// <summary>"generated" (question written by the n8n generator) or "manual" (added by hand).</summary>
    public string CaseType { get; set; } = BenchmarkCaseType.Generated;
    public bool IsReviewed { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>The "Generate Test Cases" request that produced this case — sent to the n8n generator and required back in
    /// its response. Null for manual cases (and for cases created before generation ids existed).</summary>
    public Guid? GenerationId { get; set; }

    /// <summary>SHA-256 (hex) of the expected chunk's content when the case was created/linked.</summary>
    public string? SourceChunkHash { get; set; }
    public string? SourceChunkPreview { get; set; }
    public string? SourceDocumentTitle { get; set; }

    public bool IsStale { get; set; }
    public string? StaleReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
