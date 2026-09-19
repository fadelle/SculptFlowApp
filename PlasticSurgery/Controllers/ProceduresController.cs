using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

[ApiController]
[Route("api/procedures")]
public class ProceduresController : DashboardApiController
{
    private readonly IProcedureService _procedures;

    public ProceduresController(IProcedureService procedures, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _procedures = procedures;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProcedureResponse>>> List([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var items = await _procedures.ListAsync(clinicId.Value, activeOnly, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProcedureResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var procedure = await _procedures.GetByIdAsync(clinicId.Value, id, ct);
        return procedure is null ? NotFound() : Ok(procedure);
    }

    [HttpPost]
    public async Task<ActionResult<ProcedureResponse>> Create([FromBody] CreateProcedureRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var procedure = await _procedures.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
            return CreatedAtAction(nameof(GetById), new { id = procedure.Id }, procedure);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProcedureResponse>> Update(Guid id, [FromBody] UpdateProcedureRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var procedure = await _procedures.UpdateAsync(clinicId.Value, id, request, ct);
            return procedure is null ? NotFound() : Ok(procedure);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Activate/deactivate — procedures are never deleted (leads, appointments and procedure
    /// bookings reference them).</summary>
    [HttpPost("{id:guid}/active")]
    public async Task<ActionResult<ProcedureResponse>> SetActive(Guid id, [FromBody] SetProcedureActiveRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var procedure = await _procedures.SetActiveAsync(clinicId.Value, id, request.IsActive, ct);
        return procedure is null ? NotFound() : Ok(procedure);
    }
}
