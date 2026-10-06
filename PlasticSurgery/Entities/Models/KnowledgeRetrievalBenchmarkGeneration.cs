using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

/// <summary>One "Generate Test Cases" request. <see cref="Id"/> IS the generationId sent to the n8n generator and required on its
/// callback. Created (pending) before anything is sent; it remembers the chunks that were sent so the reply can be validated
/// against exactly that set, and the clinic is always taken from this row — never from the caller.</summary>
public class KnowledgeRetrievalBenchmarkGeneration
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    /// <summary>pending | completed | failed | cancelled</summary>
    public string Status { get; set; } = BenchmarkGenerationStatus.Pending;

    public int ChunksSent { get; set; }
    /// <summary>JSON [{documentId, chunkId, documentTitle}] — the chunks that were sent (ids and title only, no text).</summary>
    public string? SentChunksJson { get; set; }

    public int QuestionsReturned { get; set; }
    public int CasesCreated { get; set; }
    public int RejectedCount { get; set; }
    /// <summary>JSON [{question, reason}] — the first rejected items (capped); <see cref="RejectedCount"/> is the exact total.</summary>
    public string? RejectedJson { get; set; }
    /// <summary>The reply/callback body n8n sent (capped).</summary>
    public string? RawResponse { get; set; }

    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
