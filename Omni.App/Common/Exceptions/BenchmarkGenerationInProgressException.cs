using System.Text.Json;

namespace Omni.App.Common.Exceptions;

/// <summary>A generation was requested while another is still waiting for n8n's callback.</summary>
public class BenchmarkGenerationInProgressException : InvalidOperationException
{
    public BenchmarkGenerationInProgressException()
        : base("A test-case generation is already waiting for the question generator. Press \"Stop generating\" to give up on it, or wait for it to finish or time out.") { }
}
