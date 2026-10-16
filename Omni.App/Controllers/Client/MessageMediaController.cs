using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;

namespace Omni.App.Controllers.Client;

/// <summary>Streams a message's media to the Inbox player. The clinic always comes from the signed-in user.</summary>
[ApiController]
[Route("api/messages/{messageId:guid}/media")]
public class MessageMediaController : DashboardApiController
{
    private readonly IMessageMediaService _media;

    public MessageMediaController(ICurrentClinicContext clinicContext, IMessageMediaService media) : base(clinicContext)
    {
        _media = media;
    }

    /// <summary>The audio of a voice note. Private cache: the browser may replay it without asking again, but no shared cache
    /// may keep a patient's recording. Range requests are on so the player can seek.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid messageId, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var audio = await _media.GetAudioAsync(clinicId.Value, messageId, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(audio.Bytes, audio.ContentType, enableRangeProcessing: true);
    }
}
