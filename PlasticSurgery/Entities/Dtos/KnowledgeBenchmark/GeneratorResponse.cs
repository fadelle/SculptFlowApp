using System.Text.Json;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>What the generator returned: the generationId it echoed (null if it didn't) and the questions.</summary>
public record GeneratorResponse(string? GenerationId, IReadOnlyList<RawGeneratedQuestion> Questions);
