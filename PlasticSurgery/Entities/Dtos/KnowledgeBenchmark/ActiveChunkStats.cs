namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>How many chunks of active documents a clinic has indexed, and their average length in characters.</summary>
public record ActiveChunkStats(int Count, double? AverageChars);
