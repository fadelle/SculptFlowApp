namespace Omni.App.Common.Exceptions;

/// <summary>Thrown when a call to the AI model API (IAiModelClient) fails: not configured, unreachable, timed out, an error
/// answer, or an answer that can't be read. The message never carries the provider's body (it can echo the key).</summary>
public class AiModelException : Exception
{
    public AiModelException(string message, Exception? inner = null) : base(message, inner) { }
}
