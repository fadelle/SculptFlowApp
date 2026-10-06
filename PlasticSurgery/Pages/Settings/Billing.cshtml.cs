using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Billing;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Billing: the clinic's plan, renewal date, included credit, wallet and reserved balance, this
/// period's usage and recent transactions. Read-only — top-ups and plan changes are done by SculptFlow (admin API).
/// Clinic from the logged-in user; nothing provider-specific is shown.</summary>
public class BillingModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IBillingQueryService _billing;

    public BillingModel(ICurrentClinicContext clinicContext, IBillingQueryService billing)
    {
        _clinicContext = clinicContext;
        _billing = billing;
    }

    public bool ClinicConfigured { get; private set; }
    public ClinicBillingSummary? Summary { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync(ct);
        if (clinicId is null) return;
        ClinicConfigured = true;
        Summary = await _billing.GetSummaryAsync(clinicId.Value, ct);
    }
}
