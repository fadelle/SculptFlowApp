namespace Omni.App.Entities.Dtos.Billing;

/// <summary>The client's plan usage at a glance (Billing page): each limit in use against the plan, and this period's
/// messages and AI replies (count and what Omni charged for them).</summary>
public record PlanUsageDto(
    IReadOnlyList<PlanQuotaRow> Quotas,
    int Messages,
    decimal MessagesCharged,
    int AiReplies,
    decimal AiRepliesCharged,
    bool AiIncluded);
