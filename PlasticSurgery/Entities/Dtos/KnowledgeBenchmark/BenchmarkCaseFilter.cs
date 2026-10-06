using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>Which slice of the case list to return. "all" | "generated" | "manual" | "reviewed" | "unreviewed" | "stale".</summary>
public record BenchmarkCaseFilter(string? View = null, Guid? GenerationId = null);
