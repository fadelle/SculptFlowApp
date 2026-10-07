namespace PlasticSurgery.Common.Enums;

/// <summary>How a provider webhook delivery was received; the controller turns it into the HTTP status.</summary>
public enum WebhookReceiveOutcome
{
    /// <summary>Authenticated and processed (200).</summary>
    Accepted,
    /// <summary>Unknown, disconnected or disabled endpoint (404).</summary>
    NotFound,
    /// <summary>The secret didn't match (403).</summary>
    Forbidden
}
