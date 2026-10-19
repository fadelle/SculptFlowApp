namespace Omni.App.Entities.Dtos.KnowledgeBenchmark;

/// <summary>Exact shape POSTed to N8n:KnowledgeBenchmarkWebhookUrl (camelCase JSON): the generation to answer for, the client
/// and the chunks to write questions from. Read by docs/n8n/omni-retrieval-benchmark.json; the field list is in
/// docs/system-design.html §7 (N8nContractTests checks both).</summary>
public record BenchmarkGeneratorPayload(Guid GenerationId, Guid ClientId, IReadOnlyList<BenchmarkSourceChunk> Chunks);
