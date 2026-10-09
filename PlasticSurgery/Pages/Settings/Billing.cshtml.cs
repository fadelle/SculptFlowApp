using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Billing: the clinic's plan, renewal date, included credit, wallet and reserved balance, this
/// period's usage and recent transactions. Read-only — top-ups and plan changes are done by SculptFlow (admin API).
/// Clinic from the logged-in user; nothing provider-specific is shown. Not found while billing is switched off
/// (Billing:Enabled), like its sidebar link.</summary>
public class BillingModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IBillingQueryService _billing;
    private readonly IConfigManager _config;

    public BillingModel(ICurrentClinicContext clinicContext, IBillingQueryService billing, IConfigManager config)
    {
        _clinicContext = clinicContext;
        _billing = billing;
        _config = config;
    }

    public bool ClinicConfigured { get; private set; }
    public ClinicBillingSummary? Summary { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_config.BillingEnabled) return NotFound();
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return Page();
        ClinicConfigured = true;
        Summary = await _billing.GetSummaryAsync(clinicId.Value, ct);
        return Page();
    }
}
