using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Leads;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.Leads;
using Omni.App.Entities.Responses.Leads;

namespace Omni.App.Controllers.Client;

/// <summary>
/// Lead intake and management. Dashboard actions (List/GetById/Update/UpdateStatus) require login
/// and resolve clientId server-side via DashboardApiController — never from the browser. Create is
/// the n8n intake path (see its own doc comment): no logged-in user, so it takes ClientId in the body
/// and is trusted only through the X-Ingest-Key header.
/// </summary>
[ApiController]
[Route("api/leads")]
public class LeadsController : DashboardApiController
{
    private readonly ILeadService _leads;

    public LeadsController(ILeadService leads, ICurrentClientContext clientContext) : base(clientContext)
    {
        _leads = leads;
    }

    /// <summary>Creates a lead, or returns the existing one if ExternalLeadId was already seen for
    /// this client — safe for n8n to retry without creating duplicates. Intake-only: no logged-in user
    /// is calling this, so ClientId comes from the request body, which is why the caller must hold the
    /// ingest key (without it anyone could add leads to any client). No key = 401.</summary>
    [AllowAnonymous]
    [RequireIngestKey]
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
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _leads.ListAsync(clientId.Value, status, procedureId, search, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeadResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var lead = await _leads.GetByIdAsync(clientId.Value, id, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LeadResponse>> Update(Guid id, [FromBody] UpdateLeadRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var lead = await _leads.UpdateAsync(clientId.Value, id, request, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<LeadResponse>> UpdateStatus(Guid id, [FromBody] UpdateLeadStatusRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var lead = await _leads.UpdateStatusAsync(clientId.Value, id, request, ct);
        return lead is null ? NotFound() : Ok(lead);
    }
}
