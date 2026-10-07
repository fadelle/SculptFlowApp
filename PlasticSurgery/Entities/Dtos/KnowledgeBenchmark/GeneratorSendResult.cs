using System.Text.Json;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>The outcome of handing a generation request to n8n. <see cref="Immediate"/> is set ONLY when n8n answered the
/// request itself with the questions (a workflow that responds synchronously); the normal case is null — n8n acknowledged
/// and will call back later.</summary>
public record GeneratorSendResult(GeneratorResponse? Immediate, string? RawBody);
