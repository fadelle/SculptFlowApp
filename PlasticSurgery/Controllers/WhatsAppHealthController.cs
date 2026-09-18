using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>Dashboard-facing reads for /WhatsApp/Health and the main dashboard's health indicator.
/// The n8n-facing write path is POST /api/integrations/whatsapp/health/events (WhatsAppIntegrationEventsController).</summary>
[ApiController]
[Route("api/whatsapp/health")]
public class WhatsAppHealthController : DashboardApiController
{
    private readonly IWhatsAppHealthService _health;

    public WhatsAppHealthController(IWhatsAppHealthService health, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _health = health;
    }

    [HttpGet]
    public async Task<ActionResult<WhatsAppHealthResponse>> GetHealth(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var health = await _health.GetHealthAsync(clinicId.Value, ct);
        return health is null ? NotFound() : Ok(health);
    }

    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppHealthEventResponse>>> GetHealthEvents(
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var events = await _health.GetHealthEventsAsync(clinicId.Value, skip, Math.Clamp(take, 1, 200), ct);
        return Ok(events);
    }
}
