namespace PlasticSurgery.Common.Exceptions;

/// <summary>Thrown when a free-form send is attempted while the 24h WhatsApp customer service
/// window is closed — callers should offer "Send Template" instead.</summary>
public class ServiceWindowClosedException : Exception
{
    public ServiceWindowClosedException(string message) : base(message) { }
}
