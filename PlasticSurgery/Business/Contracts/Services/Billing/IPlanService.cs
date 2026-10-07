using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.Billing;

/// <summary>Plan catalog management (admin API). Plans are never deleted — deactivate one to stop offering it;
/// clinics already on it keep it and renew at its current price.</summary>
public interface IPlanService
{
    Task<IReadOnlyList<PlanResponse>> ListAsync(CancellationToken ct = default);
    Task<PlanResponse?> GetAsync(string code, CancellationToken ct = default);
    Task<PlanResponse> CreateAsync(PlanRequest request, CancellationToken ct = default);
    /// <summary>Updates everything but the code. Entitlements are replaced when the request carries them.</summary>
    Task<PlanResponse?> UpdateAsync(string code, PlanRequest request, CancellationToken ct = default);
    /// <summary>Replaces the plan's whole entitlement set.</summary>
    Task<PlanResponse?> SetEntitlementsAsync(string code, IReadOnlyDictionary<string, string> entitlements, CancellationToken ct = default);
}
