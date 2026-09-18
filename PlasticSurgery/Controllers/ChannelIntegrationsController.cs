using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Manages per-clinic WhatsApp/Instagram/Facebook connection config. The dashboard's Settings →
/// Channels &amp; Integrations page is the primary consumer today; n8n or a future onboarding flow
/// can also read these to know which channels are live for a clinic.
/// </summary>
[ApiController]
[Route("api/channel-integrations")]
public class ChannelIntegrationsController : DashboardApiController
{
    private readonly IChannelIntegrationService _integrations;
    private readonly IMetaGraphClient _graph;

    public ChannelIntegrationsController(IChannelIntegrationService integrations, IMetaGraphClient graph, ICurrentClinicContext clinicContext)
        : base(clinicContext)
    {
        _integrations = integrations;
        _graph = graph;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelIntegrationResponse>>> List(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var items = await _integrations.ListAsync(clinicId.Value, ct);
        return Ok(items);
    }

    [HttpPut]
    public async Task<ActionResult<ChannelIntegrationResponse>> Save([FromBody] SaveChannelIntegrationRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var result = await _integrations.SaveAsync(request with { ClinicId = clinicId.Value }, ct);
        return Ok(result);
    }

    [HttpPost("{channel}/disconnect")]
    public async Task<IActionResult> Disconnect(string channel, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        await _integrations.DisconnectAsync(clinicId.Value, channel, ct);
        return NoContent();
    }

    /// <summary>Called from the browser after WhatsApp Embedded Signup (FB.login()) completes.</summary>
    [HttpPost("whatsapp/connect")]
    public async Task<ActionResult<ChannelIntegrationResponse>> ConnectWhatsApp([FromBody] ConnectWhatsAppRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var result = await _integrations.ConnectWhatsAppAsync(request with { ClinicId = clinicId.Value }, ct);
            return Ok(result);
        }
        catch (Exception ex) when (ex is MetaGraphApiException or InvalidOperationException)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway,
                title: "Could not complete WhatsApp connection.");
        }
    }

    /// <summary>
    /// DEBUG-ONLY: exchanges a WhatsApp Embedded Signup code for a token and reports exactly what
    /// scopes Meta actually granted it — used to diagnose why the phone-number picker step isn't
    /// showing up in the popup, independent of whether that step ever ran. Remove once resolved.
    /// </summary>
    [HttpPost("whatsapp/debug-token")]
    public async Task<IActionResult> DebugWhatsAppToken([FromBody] DebugTokenRequest request, CancellationToken ct)
    {
        try
        {
            var token = !string.IsNullOrWhiteSpace(request.AccessToken)
                ? request.AccessToken
                : !string.IsNullOrWhiteSpace(request.Code)
                    ? await _graph.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri, ct)
                    : throw new InvalidOperationException("Either AccessToken or Code must be provided.");
            var scopes = await _graph.GetGrantedScopesAsync(token, ct);
            var clientWabas = await _graph.GetClientWhatsAppBusinessAccountsAsync(token, ct);
            return Ok(new { scopes, clientWabas });
        }
        catch (Exception ex) when (ex is MetaGraphApiException or InvalidOperationException)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway,
                title: "Could not inspect token.");
        }
    }

    /// <summary>Called from the browser after the Facebook Login popup (FB.login()) completes.</summary>
    [HttpPost("facebook/connect")]
    public async Task<ActionResult<ChannelIntegrationResponse>> ConnectFacebook([FromBody] ConnectFacebookRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var result = await _integrations.ConnectFacebookAsync(request with { ClinicId = clinicId.Value }, ct);
            return Ok(result);
        }
        catch (Exception ex) when (ex is MetaGraphApiException or InvalidOperationException)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway,
                title: "Could not complete Facebook connection.");
        }
    }
}
