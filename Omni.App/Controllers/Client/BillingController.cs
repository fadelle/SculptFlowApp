using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Controllers.Client;

/// <summary>The clinic's own billing view (read-only): plan, renewal date, included credit, wallet, reserved,
/// usage and transactions. Clinic from the logged-in user; never shows providers, costs or margins.</summary>
[ApiController]
[Route("api/billing")]
public class BillingController : DashboardApiController
{
    private readonly IBillingQueryService _billing;

    public BillingController(ICurrentClinicContext clinicContext, IBillingQueryService billing) : base(clinicContext)
    {
        _billing = billing;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ClinicBillingSummary>> Summary(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _billing.GetSummaryAsync(clinicId.Value, ct));
    }

    [HttpGet("usage")]
    public async Task<ActionResult<PagedResponse<ClinicUsageRow>>> Usage([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _billing.ListClinicUsageAsync(clinicId.Value, page, pageSize, ct));
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<PagedResponse<BillingTransactionRow>>> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _billing.ListTransactionsAsync(clinicId.Value, page, pageSize, ct));
    }
}
