using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Trusted ingest endpoint for n8n — the one entry point for everything on a WhatsApp thread our
/// own backend didn't directly cause: a customer message, a staff reply echoed from the WhatsApp
/// Business phone app, an AI message n8n already sent, or a delivery-status update. Protected by a
/// shared-secret header (RequireIngestKey), not the open clinicId-parameter trust level the rest
/// of the MVP API uses — this endpoint can create/modify data across any clinic n8n asks for by
/// id, so it must not be reachable by the browser.
/// </summary>
[ApiController]
[Route("api/messages")]
[RequireIngestKey]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messages;

    public MessagesController(IMessageService messages)
    {
        _messages = messages;
    }

    [HttpPost("ingest")]
    public async Task<ActionResult<IngestMessageResult>> Ingest([FromBody] IngestMessageRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _messages.IngestAsync(request, ct);
            if (!result.Found)
            {
                return NotFound(new { error = "Conversation (or, for a status update, the referenced message) was not found for this clinic." });
            }
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
