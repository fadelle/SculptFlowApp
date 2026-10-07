using System.Text.Json;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>The generator webhook failed or returned something unusable.</summary>
public class BenchmarkGenerationException : Exception
{
    public BenchmarkGenerationException(string message, Exception? inner = null) : base(message, inner) { }
}
