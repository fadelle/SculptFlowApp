namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One client's AI bot use in a period (QA t7-10): runs and model calls from the message trace, tokens summed from
/// the model calls, the model cost they suggest at the configured prices (an estimate, in USD), and what the client was
/// charged for AI replies (settled ai_reply usage, after refunds, in the client's billing currency).</summary>
public record AiUsageRow(
    Guid ClientId,
    string ClientName,
    long Runs,
    long FailedRuns,
    long ModelCalls,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedModelCostUsd,
    decimal AiReplyRevenue,
    string? RevenueCurrency);
