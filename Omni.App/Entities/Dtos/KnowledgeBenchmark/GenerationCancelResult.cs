using System.Text.Json;
using Omni.App.Common.Enums;
using Omni.App.Entities.Responses.KnowledgeBenchmark;

namespace Omni.App.Entities.Dtos.KnowledgeBenchmark;

public record GenerationCancelResult(GenerationCancelStatus Status, BenchmarkGenerationSummary? Summary);
