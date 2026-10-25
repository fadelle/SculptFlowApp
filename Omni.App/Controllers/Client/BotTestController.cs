using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Mappers.Ai;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Requests.Ai;

namespace Omni.App.Controllers.Client;

/// <summary>The bot editor's test panel (wwwroot/js/bot-test.js): send a message, poll the transcript, reset. The run itself
/// happens in n8n with write actions simulated (IBotTestService).</summary>
[ApiController]
[Route("api/bots/{botId:guid}/test")]
public class BotTestController : DashboardApiController
{
    private readonly IBotTestService _tests;
    private readonly IStringLocalizer _text;

    public BotTestController(IBotTestService tests, IStringLocalizer text, ICurrentClientContext clientContext) : base(clientContext)
    {
        _tests = tests;
        _text = text;
    }

    [HttpPost("messages")]
    public async Task<ActionResult<object>> Send(Guid botId, [FromBody] SendBotTestMessageRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(new { sessionId = await _tests.SendAsync(clientId.Value, botId, request.SessionId, request.Text, ct) });
    }

    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<BotTestTranscriptDto>> Get(Guid botId, Guid sessionId, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var found = await _tests.GetAsync(clientId.Value, botId, sessionId, ct);
        return found is { } t ? Ok(BotTestMapper.ToTranscript(t.Session, t.Lines, _text)) : NotFound();
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Reset(Guid botId, Guid sessionId, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        await _tests.ResetAsync(clientId.Value, sessionId, ct);
        return NoContent();
    }
}
