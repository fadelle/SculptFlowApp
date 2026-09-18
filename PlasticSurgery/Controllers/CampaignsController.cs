using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/campaigns")]
public class CampaignsController : DashboardApiController
{
    private readonly ICampaignService _campaigns;
    private readonly ICampaignAudienceService _audience;

    public CampaignsController(ICampaignService campaigns, ICampaignAudienceService audience, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _campaigns = campaigns;
        _audience = audience;
    }

    /// <summary>Read-only "Matching Leads: N" preview for the Campaign creation page — resolves the
    /// same audience CreateAsync would snapshot, without creating anything.</summary>
    [HttpGet("audience-preview")]
    public async Task<ActionResult<AudiencePreviewResponse>> AudiencePreview(
        [FromQuery] string audienceType, [FromQuery] string? filters, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        if (!Data.Entities.CampaignAudienceType.All.Contains(audienceType))
        {
            return BadRequest(new { error = $"Unknown audience type '{audienceType}'." });
        }

        var count = await _audience.GetMatchingCountAsync(clinicId.Value, audienceType, filters, ct);
        return Ok(new AudiencePreviewResponse(count));
    }

    /// <summary>"Preview leads" on the Campaign creation page — the same audience resolution as
    /// AudiencePreview above (ICampaignAudienceService.GetEligibleLeadsAsync), just also returning
    /// up to `limit` names/phones instead of only a count. Read-only, no side effects.</summary>
    [HttpGet("audience-preview/leads")]
    public async Task<ActionResult<AudiencePreviewLeadsResponse>> AudiencePreviewLeads(
        [FromQuery] string audienceType, [FromQuery] string? filters, [FromQuery] int limit, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        if (!Data.Entities.CampaignAudienceType.All.Contains(audienceType))
        {
            return BadRequest(new { error = $"Unknown audience type '{audienceType}'." });
        }

        var cappedLimit = limit is > 0 and <= 50 ? limit : 10;
        var leads = await _audience.GetEligibleLeadsAsync(clinicId.Value, audienceType, filters, ct);
        var preview = leads.Take(cappedLimit).Select(l => new AudiencePreviewLeadRow(l.Id, l.FullName, l.Phone)).ToList();
        return Ok(new AudiencePreviewLeadsResponse(leads.Count, preview));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CampaignListRow>>> List(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var campaigns = await _campaigns.ListAsync(clinicId.Value, ct);
        return Ok(campaigns);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDetailsResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var details = await _campaigns.GetByIdAsync(clinicId.Value, id, ct);
        return details is null ? NotFound() : Ok(details);
    }

    [HttpPost]
    public async Task<ActionResult<CampaignResponse>> Create([FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var campaign = await _campaigns.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
            return CreatedAtAction(nameof(GetById), new { id = campaign.Id }, campaign);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public record ScheduleCampaignRequest(DateTimeOffset ScheduledAt);

    [HttpPost("{id:guid}/schedule")]
    public async Task<ActionResult<CampaignResponse>> Schedule(Guid id, [FromBody] ScheduleCampaignRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var campaign = await _campaigns.ScheduleAsync(clinicId.Value, id, request.ScheduledAt, ct);
            return campaign is null ? NotFound() : Ok(campaign);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    /// <summary>Starts (or, if already Running, continues) sending — queues every Pending recipient
    /// on first call, then processes one bounded batch. Safe to call repeatedly; keep calling until
    /// the result's CampaignCompleted is true. See ICampaignService for the batching rationale.</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<ProcessCampaignBatchResult>> Send(Guid id, [FromQuery] int batchSize = 20, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var result = await _campaigns.SendAsync(clinicId.Value, id, batchSize, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    /// <summary>Processes another bounded batch of an already-Running campaign — the endpoint an n8n
    /// schedule (or a "Send more" dashboard button) calls repeatedly until it's done.</summary>
    [HttpPost("{id:guid}/process-batch")]
    public async Task<ActionResult<ProcessCampaignBatchResult>> ProcessBatch(Guid id, [FromQuery] int batchSize = 20, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var result = await _campaigns.ProcessBatchAsync(clinicId.Value, id, batchSize, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<CampaignResponse>> Cancel(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var campaign = await _campaigns.CancelAsync(clinicId.Value, id, ct);
            return campaign is null ? NotFound() : Ok(campaign);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }
}
