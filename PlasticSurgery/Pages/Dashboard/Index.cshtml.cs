using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>
/// Page 1 — Main Numbers. Calls the same application services the REST API controllers call
/// (IDashboardService etc.) rather than making an HTTP round-trip to its own API — same business
/// logic either way, without the extra network hop and JSON round-trip inside one process.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IDashboardService _dashboard;
    private readonly IWhatsAppHealthService _whatsAppHealth;

    public IndexModel(ICurrentClinicContext clinicContext, IDashboardService dashboard, IWhatsAppHealthService whatsAppHealth)
    {
        _clinicContext = clinicContext;
        _dashboard = dashboard;
        _whatsAppHealth = whatsAppHealth;
    }

    public string? ClinicName { get; private set; }
    public bool ClinicConfigured { get; private set; }
    public Guid ClinicId { get; private set; }
    public DashboardSummaryResponse? Summary { get; private set; }
    /// <summary>Null means "no WhatsApp connection yet" — the indicator shows a neutral state, not
    /// a warning, since an unconnected number isn't itself a problem for a brand-new clinic.</summary>
    public WhatsAppHealthResponse? WhatsAppHealth { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        ClinicId = clinic.Id;
        ClinicName = clinic.Name;
        Summary = await _dashboard.GetSummaryAsync(clinic.Id, ct);
        WhatsAppHealth = await _whatsAppHealth.GetHealthAsync(clinic.Id, ct);
    }
}
