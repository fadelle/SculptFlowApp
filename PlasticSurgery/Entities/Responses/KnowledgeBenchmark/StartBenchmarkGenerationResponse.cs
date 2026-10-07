using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

/// <summary>The result of clicking "Generate Test Cases". Normally <see cref="Status"/> is "pending": the chunks were handed to n8n
/// and its callback will create the cases later (poll GET generations/{id}). "completed" only when the workflow answered
/// synchronously; "none" when there was nothing to sample (then <see cref="GenerationId"/> is null).</summary>
public record StartBenchmarkGenerationResponse(
    /// <summary>The id sent to n8n (and carried by its callback); null when nothing was sent.</summary>
    Guid? GenerationId,
    string Status,
    /// <summary>How many chunks were sampled and sent to the generator.</summary>
    int ChunksSent,
    /// <summary>Only meaningful when Status is "completed" (a synchronous reply).</summary>
    int QuestionsReturned,
    int CasesCreated,
    IReadOnlyList<RejectedGeneratedQuestion> Rejected,
    string? Message);
