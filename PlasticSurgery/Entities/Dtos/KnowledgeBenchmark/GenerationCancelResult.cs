using System.Text.Json;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

public record GenerationCancelResult(GenerationCancelStatus Status, BenchmarkGenerationSummary? Summary);
