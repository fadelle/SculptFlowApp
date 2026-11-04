namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A conversation whose last message is the customer's and got no answer (QA t7-10): who, where, since when, and
/// what the trace of that message came to (Outcome: a TraceResults value; null when the message has no trace).</summary>
public record UnansweredRow(
    Guid ConversationId,
    Guid ClientId,
    string ClientName,
    string? ContactName,
    string Channel,
    string Mode,
    DateTimeOffset LastCustomerMessageAt,
    Guid? MessageId,
    string? MessageType,
    string? TraceId,
    string? Outcome,
    string? OutcomeReason);
