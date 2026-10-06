using System.Text.Json;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>One question exactly as the generator returned it. Deliberately UNTRUSTED raw strings: the benchmark
/// service validates every id against the generation's sent chunks and the current clinic before anything is stored.</summary>
public record RawGeneratedQuestion(string? DocumentId, string? ChunkId, string? Question);
