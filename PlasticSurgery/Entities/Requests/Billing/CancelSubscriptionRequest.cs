namespace PlasticSurgery.Entities.Requests.Billing;

public record CancelSubscriptionRequest(bool Immediately = false, string? Reason = null);
