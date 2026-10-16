using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Procedures;
using Omni.App.Entities.Requests.Procedures;
using Omni.App.Entities.Responses.Procedures;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/procedures")]
public class ProceduresController : DashboardApiController
{
    private readonly IProcedureService _procedures;

    public ProceduresController(IProcedureService procedures, ICurrentClientContext clientContext) : base(clientContext)
    {
        _procedures = procedures;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProcedureResponse>>> List([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var items = await _procedures.ListAsync(clientId.Value, activeOnly, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProcedureResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var procedure = await _procedures.GetByIdAsync(clientId.Value, id, ct);
        return procedure is null ? NotFound() : Ok(procedure);
    }

    [HttpPost]
    public async Task<ActionResult<ProcedureResponse>> Create([FromBody] CreateProcedureRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var procedure = await _procedures.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(GetById), new { id = procedure.Id }, procedure);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProcedureResponse>> Update(Guid id, [FromBody] UpdateProcedureRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var procedure = await _procedures.UpdateAsync(clientId.Value, id, request, ct);
        return procedure is null ? NotFound() : Ok(procedure);
    }

    /// <summary>Activate/deactivate — procedures are never deleted (leads, appointments and procedure
    /// bookings reference them).</summary>
    [HttpPost("{id:guid}/active")]
    public async Task<ActionResult<ProcedureResponse>> SetActive(Guid id, [FromBody] SetProcedureActiveRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var procedure = await _procedures.SetActiveAsync(clientId.Value, id, request.IsActive, ct);
        return procedure is null ? NotFound() : Ok(procedure);
    }
}
