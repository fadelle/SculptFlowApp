using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Entities.Responses.Billing;

public record SubscriptionResponse(Guid Id, Guid ClientId, string PlanCode, string PlanName, string Status,
    DateTimeOffset CurrentPeriodStart, DateTimeOffset CurrentPeriodEnd, bool CancelAtPeriodEnd, DateTimeOffset? PastDueSince,
    DateTimeOffset? EndedAt)
{
    /// <summary>Set only by a plan change: each limit the client now uses more of than the plan allows (QA t2-billing-11);
    /// empty when everything fits.</summary>
    public IReadOnlyList<PlanQuotaRow>? OverLimits { get; init; }
}
