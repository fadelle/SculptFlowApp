using System.Text.Json;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>A generation was requested while another is still waiting for n8n's callback.</summary>
public class BenchmarkGenerationInProgressException : InvalidOperationException
{
    public BenchmarkGenerationInProgressException()
        : base("A test-case generation is already waiting for the question generator — wait for it to finish (or for it to time out).") { }
}
