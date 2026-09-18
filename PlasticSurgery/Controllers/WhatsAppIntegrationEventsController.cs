using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Trusted entry points for an already-normalized template or health event (from tooling, tests, or
/// a caller that already classified it itself) — protected the same way as MessagesController: a
/// shared-secret header, not open to the browser. Their Handler equivalents inside
/// Integrations/WhatsApp/MetaWebhookProcessor.cs call the exact same underlying services these two
/// actions do, so there's one persistence/SignalR implementation regardless of which door an event
/// came through.
///
/// The raw Meta webhook itself no longer comes through here — Meta now posts directly to
/// Controllers/WhatsAppWebhookController.cs (POST /api/integrations/whatsapp/webhook, deliberately
/// unauthenticated since Meta can't present this controller's shared-secret header), which reuses
/// the same MetaWebhookProcessor this controller's siblings do.
/// </summary>
[ApiController]
[Route("api/integrations/whatsapp")]
[RequireIngestKey]
public class WhatsAppIntegrationEventsController : ControllerBase
{
    private readonly IWhatsAppTemplateService _templates;
    private readonly IWhatsAppHealthService _health;

    public WhatsAppIntegrationEventsController(IWhatsAppTemplateService templates, IWhatsAppHealthService health)
    {
        _templates = templates;
        _health = health;
    }

    [HttpPost("templates/events")]
    public async Task<ActionResult<WhatsAppTemplateResponse>> TemplateEvent([FromBody] WhatsAppTemplateEventRequest request, CancellationToken ct)
    {
        try
        {
            var template = await _templates.ApplyMetaEventAsync(request, ct);
            return Ok(template);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpPost("health/events")]
    public async Task<ActionResult<WhatsAppHealthResponse>> HealthEvent([FromBody] WhatsAppHealthEventRequest request, CancellationToken ct)
    {
        try
        {
            var health = await _health.ApplyHealthEventAsync(request, ct);
            return Ok(health);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }
}
