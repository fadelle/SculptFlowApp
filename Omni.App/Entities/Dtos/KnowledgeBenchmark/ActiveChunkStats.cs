namespace Omni.App.Entities.Dtos.KnowledgeBenchmark;

/// <summary>How many chunks of active documents a client has indexed, and their average length in characters.</summary>
public record ActiveChunkStats(int Count, double? AverageChars);
