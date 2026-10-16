namespace Omni.App.Entities.Responses.Billing;

public record SubscriptionResponse(Guid Id, Guid ClientId, string PlanCode, string PlanName, string Status,
    DateTimeOffset CurrentPeriodStart, DateTimeOffset CurrentPeriodEnd, bool CancelAtPeriodEnd, DateTimeOffset? PastDueSince,
    DateTimeOffset? EndedAt);
