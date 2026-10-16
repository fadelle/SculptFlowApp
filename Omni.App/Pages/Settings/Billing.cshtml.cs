using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Billing: the client's plan, renewal date, included credit, wallet and reserved balance, this
/// period's usage and recent transactions. Read-only — top-ups and plan changes are done by Omni (admin API).
/// Client from the logged-in user; nothing provider-specific is shown. Not found while billing is switched off
/// (Billing:Enabled), like its sidebar link.</summary>
public class BillingModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IBillingQueryService _billing;
    private readonly IConfigManager _config;

    public BillingModel(ICurrentClientContext clientContext, IBillingQueryService billing, IConfigManager config)
    {
        _clientContext = clientContext;
        _billing = billing;
        _config = config;
    }

    public bool ClientConfigured { get; private set; }
    public ClientBillingSummary? Summary { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_config.BillingEnabled) return NotFound();
        var clientId = await _clientContext.GetClientIdAsync(ct);
        if (clientId is null) return Page();
        ClientConfigured = true;
        Summary = await _billing.GetSummaryAsync(clientId.Value, ct);
        return Page();
    }
}
