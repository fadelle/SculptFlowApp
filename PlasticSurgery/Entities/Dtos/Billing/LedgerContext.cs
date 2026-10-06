using System.Data;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>Who/why for a ledger entry (auditing: source, actor, reason, correlation, related entity).</summary>
internal sealed record LedgerContext(
    string Source,
    string? Actor = null,
    string? Reason = null,
    Guid? UsageRecordId = null,
    Guid? SubscriptionId = null,
    Guid? PlanId = null,
    string? Reference = null,
    string? CorrelationId = null);
