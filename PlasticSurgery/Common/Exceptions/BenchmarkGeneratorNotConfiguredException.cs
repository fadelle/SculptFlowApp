using System.Text.Json;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>The generator webhook is missing from configuration.</summary>
public class BenchmarkGeneratorNotConfiguredException : Exception
{
    public BenchmarkGeneratorNotConfiguredException(string message) : base(message) { }
}
