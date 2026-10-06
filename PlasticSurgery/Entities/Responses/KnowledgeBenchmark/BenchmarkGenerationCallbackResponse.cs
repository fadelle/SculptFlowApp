namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

/// <summary>What the n8n callback endpoint answers.</summary>
public record BenchmarkGenerationCallbackResponse(Guid GenerationId, string Status, int CasesCreated, int Rejected, bool AlreadyProcessed);
