using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Channels;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Requests.Channels;
using Omni.App.Entities.Responses.Channels;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Client;

/// <summary>
/// Manages per-client WhatsApp/Instagram/Facebook connection config. The dashboard's Settings →
/// Channels &amp; Integrations page is the primary consumer today; n8n or a future onboarding flow
/// can also read these to know which channels are live for a client.
/// </summary>
[ApiController]
[Route("api/channel-integrations")]
public class ChannelIntegrationsController : DashboardApiController
{
    private const string WhatsAppConnectFailedKey = "channels.errors.whatsAppConnectFailed";
    private const string FacebookConnectFailedKey = "channels.errors.facebookConnectFailed";

    private readonly IChannelIntegrationService _integrations;
    private readonly ITelegramIntegrationService _telegram;

    public ChannelIntegrationsController(
        IChannelIntegrationService integrations, ITelegramIntegrationService telegram,
        ICurrentClientContext clientContext)
        : base(clientContext)
    {
        _integrations = integrations;
        _telegram = telegram;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelIntegrationResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var items = await _integrations.ListAsync(clientId.Value, ct);
        return Ok(items);
    }

    [HttpPut]
    public async Task<ActionResult<ChannelIntegrationResponse>> Save([FromBody] SaveChannelIntegrationRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var result = await _integrations.SaveAsync(request with { ClientId = clientId.Value }, ct);
        return Ok(result);
    }

    /// <summary>Connects (or reconnects) the client's Telegram bot from a BotFather token: validates it
    /// with getMe, registers the webhook (setWebhook + secret token) and stores the connection. The
    /// token is write-only — never echoed back. clientId always comes from the logged-in user.</summary>
    [HttpPost("telegram/connect")]
    public async Task<ActionResult<ChannelIntegrationResponse>> ConnectTelegram([FromBody] ConnectTelegramRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            return Ok(await _telegram.ConnectAsync(clientId.Value, request.BotToken, ct));
        }
        catch (Exception ex) when (ex is ArgumentException or TelegramApiException)
        {
            return BadRequest(new { error = ApiErrorsAttribute.Localize(HttpContext, ex) });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ApiErrorsAttribute.Localize(HttpContext, ex) });
        }
    }

    /// <summary>Re-checks the webhook with Telegram (getWebhookInfo) and updates the stored status.</summary>
    [HttpPost("telegram/refresh")]
    public async Task<ActionResult<ChannelIntegrationResponse>> RefreshTelegram(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(await _telegram.RefreshStatusAsync(clientId.Value, ct));
    }

    [HttpPost("{channel}/disconnect")]
    public async Task<IActionResult> Disconnect(string channel, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        await _integrations.DisconnectAsync(clientId.Value, channel, ct);
        return NoContent();
    }

    /// <summary>Called from the browser after WhatsApp Embedded Signup (FB.login()) completes.</summary>
    [HttpPost("whatsapp/connect")]
    public async Task<ActionResult<ChannelIntegrationResponse>> ConnectWhatsApp([FromBody] ConnectWhatsAppRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var result = await _integrations.ConnectWhatsAppAsync(request with { ClientId = clientId.Value }, ct);
            return Ok(result);
        }
        catch (Exception ex) when (ex is MetaGraphApiException or InvalidOperationException)
        {
            return Problem(detail: ApiErrorsAttribute.Localize(HttpContext, ex), statusCode: StatusCodes.Status502BadGateway,
                title: ApiErrorsAttribute.Text(HttpContext, WhatsAppConnectFailedKey));
        }
    }

    /// <summary>Called from the browser after the Facebook Login popup (FB.login()) completes.</summary>
    [HttpPost("facebook/connect")]
    public async Task<ActionResult<ChannelIntegrationResponse>> ConnectFacebook([FromBody] ConnectFacebookRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var result = await _integrations.ConnectFacebookAsync(request with { ClientId = clientId.Value }, ct);
            return Ok(result);
        }
        catch (Exception ex) when (ex is MetaGraphApiException or InvalidOperationException)
        {
            return Problem(detail: ApiErrorsAttribute.Localize(HttpContext, ex), statusCode: StatusCodes.Status502BadGateway,
                title: ApiErrorsAttribute.Text(HttpContext, FacebookConnectFailedKey));
        }
    }
}
