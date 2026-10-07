using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.PlatformAdmin;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin clinics API (internal; called by the SculptFlowAdmin portal). Writes answer { clinicId } so the portal
/// can file its audit entry. Errors: 400 invalid input, 404 no such clinic.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/clinics")]
[ApiErrors]
public class ClinicsAdminController : ControllerBase
{
    private readonly IClinicAdminService _clinics;

    public ClinicsAdminController(IClinicAdminService clinics)
    {
        _clinics = clinics;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool? active, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _clinics.ListAsync(search, active, page, ct));

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct) => Ok(await _clinics.OptionsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await _clinics.GetAsync(id, ct) is { } clinic ? Ok(clinic) : NotFound();

    [HttpGet("{id:guid}/counts")]
    public async Task<IActionResult> Counts(Guid id, CancellationToken ct) => Ok(await _clinics.CountsAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ClinicUpdate request, CancellationToken ct) =>
        Ok(await _clinics.UpdateAsync(id, request, ct));

    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _clinics.SetActiveAsync(id, request.Active, ct));
}
