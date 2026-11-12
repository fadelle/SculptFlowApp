namespace Omni.App.Entities.Requests.Billing;

/// <param name="AllowAnotherTrial">Start the trial plan for a client that already had a trial (QA t2-billing-21).</param>
public record StartSubscriptionApiRequest(string PlanCode, bool ChargeFirstPeriod = true, string? Reason = null, bool AllowAnotherTrial = false);
