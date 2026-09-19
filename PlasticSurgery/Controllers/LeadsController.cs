using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Lead intake and management. Dashboard actions (List/GetById/Update/UpdateStatus) require login
/// and resolve clinicId server-side via DashboardApiController — never from the browser. Create
/// stays open: it's the n8n/Meta-webhook intake path (see its own doc comment), which has no logged
/// -in user to resolve a clinic from, so it still takes ClinicId explicitly in the body.
/// </summary>
[ApiController]
[Route("api/leads")]
public class LeadsController : DashboardApiController
{
    private readonly ILeadService _leads;

    public LeadsController(ILeadService leads, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _leads = leads;
    }

    /// <summary>Creates a lead, or returns the existing one if ExternalLeadId was already seen for
    /// this clinic — safe for n8n/Meta webhooks to retry without creating duplicates. Intake-only:
    /// no logged-in user is calling this, so ClinicId comes from the trusted caller's request body,
    /// same as before. (Not currently ingest-key protected — flagged as a pre-existing gap, not
    /// something this change introduces.)</summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<ActionResult<LeadResponse>> Create([FromBody] CreateLeadRequest request, CancellationToken ct)
    {
        var (lead, wasCreated) = await _leads.CreateOrGetAsync(request, ct);
        return wasCreated
            ? CreatedAtAction(nameof(GetById), new { id = lead.Id }, lead)
            : Ok(lead);
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? procedureId,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _leads.ListAsync(clinicId.Value, status, procedureId, search, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeadResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var lead = await _leads.GetByIdAsync(clinicId.Value, id, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LeadResponse>> Update(Guid id, [FromBody] UpdateLeadRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var lead = await _leads.UpdateAsync(clinicId.Value, id, request, ct);
            return lead is null ? NotFound() : Ok(lead);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<LeadResponse>> UpdateStatus(Guid id, [FromBody] UpdateLeadStatusRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var lead = await _leads.UpdateStatusAsync(clinicId.Value, id, request, ct);
            return lead is null ? NotFound() : Ok(lead);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
