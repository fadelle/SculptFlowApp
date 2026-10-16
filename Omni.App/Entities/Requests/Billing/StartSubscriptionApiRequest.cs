namespace Omni.App.Entities.Requests.Billing;

public record StartSubscriptionApiRequest(string PlanCode, bool ChargeFirstPeriod = true, string? Reason = null);
