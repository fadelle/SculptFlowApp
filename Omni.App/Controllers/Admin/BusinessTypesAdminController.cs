using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.BusinessTypes;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin business types API (internal; called by the Omni.Admin portal): list, read, create, edit (names, words,
/// AI profile, stages) and switch a type on or off. Writes answer the saved type. A client's own type is changed on
/// /api/platform-admin/clients/{id}/business-type. Errors: 400 invalid input, 404 unknown code, 409 code taken,
/// 422 a stage still has contacts or it is the last active type.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/business-types")]
[ApiErrors]
public class BusinessTypesAdminController : ControllerBase
{
    private readonly IBusinessTypeService _types;

    public BusinessTypesAdminController(IBusinessTypeService types)
    {
        _types = types;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _types.ListAsync(ct));

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct) => Ok(await _types.GetAsync(code, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveBusinessTypeRequest request, CancellationToken ct) =>
        Ok(await _types.CreateAsync(request, ct));

    [HttpPut("{code}")]
    public async Task<IActionResult> Update(string code, [FromBody] SaveBusinessTypeRequest request, CancellationToken ct) =>
        Ok(await _types.UpdateAsync(code, request, ct));

    [HttpPut("{code}/active")]
    public async Task<IActionResult> SetActive(string code, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _types.SetActiveAsync(code, request.Active, ct));
}
