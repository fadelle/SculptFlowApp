using System.Text.Json;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>Thrown when a Meta Graph API call returns a non-success response — the message carries Meta's raw error body.</summary>
public class MetaGraphApiException : Exception
{
    public MetaGraphApiException(string message) : base(message) { }
}
