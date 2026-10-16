using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Controllers.Client;

/// <summary>The client's own billing view (read-only): plan, renewal date, included credit, wallet, reserved,
/// usage and transactions. Client from the logged-in user; never shows providers, costs or margins.</summary>
[ApiController]
[Route("api/billing")]
public class BillingController : DashboardApiController
{
    private readonly IBillingQueryService _billing;

    public BillingController(ICurrentClientContext clientContext, IBillingQueryService billing) : base(clientContext)
    {
        _billing = billing;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ClientBillingSummary>> Summary(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _billing.GetSummaryAsync(clientId.Value, ct));
    }

    [HttpGet("usage")]
    public async Task<ActionResult<PagedResponse<ClientUsageRow>>> Usage([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _billing.ListClientUsageAsync(clientId.Value, page, pageSize, ct));
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<PagedResponse<BillingTransactionRow>>> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _billing.ListTransactionsAsync(clientId.Value, page, pageSize, ct));
    }
}
