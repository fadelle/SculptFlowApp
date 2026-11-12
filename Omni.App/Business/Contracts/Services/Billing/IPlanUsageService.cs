using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>What the client uses of its plan: seats, connected channels, WhatsApp numbers and active AI bots against the
/// plan's limits, plus this period's messages and AI replies (from the billing summary already loaded).</summary>
public interface IPlanUsageService
{
    Task<PlanUsageDto> GetAsync(Guid clientId, ClientBillingSummary summary, CancellationToken ct = default);

    /// <summary>The limits the client is over right now (used above a non-null limit), counted as <see cref="GetAsync"/> does:
    /// seats, connected channels, WhatsApp numbers and active AI bots (QA t2-billing-11).</summary>
    Task<IReadOnlyList<PlanQuotaRow>> OverLimitsAsync(Guid clientId, CancellationToken ct = default);
}
