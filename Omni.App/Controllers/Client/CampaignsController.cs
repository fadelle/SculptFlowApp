using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Requests.Campaigns;
using Omni.App.Entities.Responses.Campaigns;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/campaigns")]
public class CampaignsController : DashboardApiController
{
    private readonly ICampaignService _campaigns;
    private readonly ICampaignAudienceService _audience;

    public CampaignsController(ICampaignService campaigns, ICampaignAudienceService audience, ICurrentClientContext clientContext) : base(clientContext)
    {
        _campaigns = campaigns;
        _audience = audience;
    }

    /// <summary>Read-only "Matching Contacts: N" preview for the Campaign creation page — resolves the
    /// same audience CreateAsync would snapshot, without creating anything.</summary>
    [HttpGet("audience-preview")]
    public async Task<ActionResult<AudiencePreviewResponse>> AudiencePreview(
        [FromQuery] string audienceType, [FromQuery] string? filters, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        if (!CampaignAudienceType.All.Contains(audienceType))
        {
            return BadRequest(new { error = $"Unknown audience type '{audienceType}'." });
        }

        var count = await _audience.GetMatchingCountAsync(clientId.Value, audienceType, filters, ct);
        return Ok(new AudiencePreviewResponse(count));
    }

    /// <summary>"Preview contacts" on the Campaign creation page — the same audience resolution as
    /// AudiencePreview above (ICampaignAudienceService.GetEligibleContactsAsync), just also returning
    /// up to `limit` names/phones instead of only a count. Read-only, no side effects.</summary>
    [HttpGet("audience-preview/contacts")]
    public async Task<ActionResult<AudiencePreviewContactsResponse>> AudiencePreviewContacts(
        [FromQuery] string audienceType, [FromQuery] string? filters, [FromQuery] int limit, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        if (!CampaignAudienceType.All.Contains(audienceType))
        {
            return BadRequest(new { error = $"Unknown audience type '{audienceType}'." });
        }

        var cappedLimit = limit is > 0 and <= 50 ? limit : 10;
        var contacts = await _audience.GetEligibleContactsAsync(clientId.Value, audienceType, filters, ct);
        var preview = contacts.Take(cappedLimit).Select(l => new AudiencePreviewContactRow(l.Id, l.FullName, l.Phone)).ToList();
        return Ok(new AudiencePreviewContactsResponse(contacts.Count, preview));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CampaignListRow>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var campaigns = await _campaigns.ListAsync(clientId.Value, ct);
        return Ok(campaigns);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDetailsResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var details = await _campaigns.GetByIdAsync(clientId.Value, id, ct);
        return details is null ? NotFound() : Ok(details);
    }

    [HttpPost]
    public async Task<ActionResult<CampaignResponse>> Create([FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var campaign = await _campaigns.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(GetById), new { id = campaign.Id }, campaign);
    }

    [HttpPost("{id:guid}/schedule")]
    public async Task<ActionResult<CampaignResponse>> Schedule(Guid id, [FromBody] ScheduleCampaignRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var campaign = await _campaigns.ScheduleAsync(clientId.Value, id, request.ScheduledAt, ct);
        return campaign is null ? NotFound() : Ok(campaign);
    }

    /// <summary>Starts (or, if already Running, continues; if Paused, resumes) sending — queues every Pending
    /// recipient, then processes one bounded batch. The background sender sends the rest, so one call is
    /// enough; calling again is safe. See ICampaignService for the batching rationale.</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<ProcessCampaignBatchResult>> Send(Guid id, [FromQuery] int batchSize = 20, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var result = await _campaigns.SendAsync(clientId.Value, id, batchSize, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Processes another bounded batch of an already-Running campaign — the endpoint an n8n
    /// schedule (or a "Send more" dashboard button) calls repeatedly until it's done.</summary>
    [HttpPost("{id:guid}/process-batch")]
    public async Task<ActionResult<ProcessCampaignBatchResult>> ProcessBatch(Guid id, [FromQuery] int batchSize = 20, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var result = await _campaigns.ProcessBatchAsync(clientId.Value, id, batchSize, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Stops a scheduled or sending campaign (status paused): nothing more goes out until it is resumed with
    /// /send, or ended with /cancel. 422 when it isn't scheduled or sending.</summary>
    [HttpPost("{id:guid}/stop")]
    public async Task<ActionResult<CampaignResponse>> Stop(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var campaign = await _campaigns.StopAsync(clientId.Value, id, ct);
        return campaign is null ? NotFound() : Ok(campaign);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<CampaignResponse>> Cancel(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var campaign = await _campaigns.CancelAsync(clientId.Value, id, ct);
        return campaign is null ? NotFound() : Ok(campaign);
    }
}
