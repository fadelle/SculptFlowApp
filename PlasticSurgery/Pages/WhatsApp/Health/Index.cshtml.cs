using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.WhatsApp.Health;

/// <summary>WhatsApp account/phone-number health — a simplified, staff-facing view over
/// ChannelIntegration's health fields (populated by Meta webhooks via n8n). See WhatsAppHealthService.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IWhatsAppHealthService _health;

    public IndexModel(ICurrentClinicContext clinicContext, IWhatsAppHealthService health)
    {
        _clinicContext = clinicContext;
        _health = health;
    }

    public bool ClinicConfigured { get; private set; }
    public Guid ClinicId { get; private set; }
    public WhatsAppHealthResponse? Health { get; private set; }
    public IReadOnlyList<WhatsAppHealthEventResponse> Events { get; private set; } = Array.Empty<WhatsAppHealthEventResponse>();

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
        Health = await _health.GetHealthAsync(clinic.Id, ct);
        Events = await _health.GetHealthEventsAsync(clinic.Id, skip: 0, take: 25, ct);
    }
}
