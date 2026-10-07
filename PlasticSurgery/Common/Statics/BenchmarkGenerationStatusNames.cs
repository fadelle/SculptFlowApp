using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace PlasticSurgery.Common.Statics;

/// <summary>Status names used in responses (kept next to the controller so the wire values are visible in one place).</summary>
internal static class BenchmarkGenerationStatusNames
{
    public const string Completed = "completed";
}
