using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// Trusted ingest endpoint for n8n: things on an EXISTING thread our own backend didn't directly cause — a customer
/// message, a member reply echoed from the WhatsApp Business phone app, an AI message n8n already sent, or a
/// delivery-status update. It never creates a contact or a conversation and never triggers the AI (its answer says
/// aiEligible): new contacts and threads come only through the signed channel webhooks (Meta, Telegram), which also
/// trigger the bot (QA t8-04, decided 2026-11-07). Protected by a
/// shared-secret header (RequireIngestKey), not the open clientId-parameter trust level the rest
/// of the MVP API uses — this endpoint can create/modify data across any client n8n asks for by
/// id, so it must not be reachable by the browser.
/// </summary>
[ApiController]
[Route("api/messages")]
[RequireIngestKey]
[ApiErrors]
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
        var result = await _messages.IngestAsync(request, ct);
        if (!result.Found)
        {
            // QA t8-04: say what the endpoint does instead of a bare "not found".
            return NotFound(new
            {
                error = "Conversation (or, for a status update, the referenced message) was not found for this client. " +
                        "Ingest only adds to an existing conversation: new contacts and conversations come in through the channel webhooks.",
                code = "conversation_required"
            });
        }
        return Ok(result);
    }
}
