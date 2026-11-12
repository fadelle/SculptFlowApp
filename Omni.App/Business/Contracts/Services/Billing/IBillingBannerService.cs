using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Business.Contracts.Services.Billing;

/// <summary>What the client should know about its plan right now, on every dashboard page (QA t2-billing-12): no plan, an
/// unpaid renewal, a trial ending soon or a low balance; null when nothing (or billing is off).</summary>
public interface IBillingBannerService
{
    Task<BillingBanner?> GetAsync(Guid clientId, CancellationToken ct = default);
}
