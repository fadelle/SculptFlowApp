namespace PlasticSurgery.Entities.Responses.Billing;

public record SubscriptionResponse(Guid Id, Guid ClinicId, string PlanCode, string PlanName, string Status,
    DateTimeOffset CurrentPeriodStart, DateTimeOffset CurrentPeriodEnd, bool CancelAtPeriodEnd, DateTimeOffset? PastDueSince,
    DateTimeOffset? EndedAt);
