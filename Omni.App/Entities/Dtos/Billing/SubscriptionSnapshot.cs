namespace Omni.App.Entities.Dtos.Billing;

/// <summary>
/// The parts of a client's subscription that entitlement checks need, cached by EntitlementService. Access is worked out
/// from it on every read (the grace period depends on the current time), so only the stored facts are cached.
/// PlanCode is null when the client has no subscription or plan.
/// </summary>
public record SubscriptionSnapshot(string? Status, DateTimeOffset? PastDueSince, string? PlanCode, string? PlanName,
    DateTimeOffset? CurrentPeriodEnd, Dictionary<string, string> Entitlements);
