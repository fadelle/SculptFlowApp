using System.Text.Json;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

public record GenerationReceiveResult(GenerationReceiveStatus Status, BenchmarkGenerationSummary? Summary, string? Message, IReadOnlyList<RejectedGeneratedQuestion>? Rejected = null);
