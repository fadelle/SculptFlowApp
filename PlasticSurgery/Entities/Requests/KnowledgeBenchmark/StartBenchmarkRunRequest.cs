namespace PlasticSurgery.Entities.Requests.KnowledgeBenchmark;

/// <summary>GenerationId is required when Scope is "generation" (and only then) — it must be one of THIS clinic's
/// generations. That run then scores exactly that generation's cases, whatever their type/reviewed state.</summary>
public record StartBenchmarkRunRequest(string? Scope, Guid? GenerationId = null);
