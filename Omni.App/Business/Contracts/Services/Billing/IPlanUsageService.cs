using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>What the client uses of its plan: seats, connected channels, WhatsApp numbers and active AI bots against the
/// plan's limits, plus this period's messages and AI replies (from the billing summary already loaded).</summary>
public interface IPlanUsageService
{
    Task<PlanUsageDto> GetAsync(Guid clientId, ClientBillingSummary summary, CancellationToken ct = default);
}
