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

    [HttpPost]
    public async Task<ActionResult<ProcedureResponse>> Create([FromBody] CreateProcedureRequest request, CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        var procedure = await _procedures.CreateAsync(request with { ClinicId = clinicId.Value }, ct);
        return CreatedAtAction(nameof(List), null, procedure);
    }
}
