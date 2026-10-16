using System.Text.Json;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.KnowledgeBenchmark;

namespace Omni.App.Entities.Dtos.KnowledgeBenchmark;

public record GenerationReceiveResult(GenerationReceiveStatus Status, BenchmarkGenerationSummary? Summary, string? Message, IReadOnlyList<RejectedGeneratedQuestion>? Rejected = null);
