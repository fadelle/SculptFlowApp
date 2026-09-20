using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>The users of the logged-in user's clinic. Login required; the clinic always comes from
/// CurrentClinicContext, never from the request.</summary>
[ApiController]
[Route("api/staff")]
public class StaffController : DashboardApiController
{
    private readonly IStaffService _staff;

    public StaffController(IStaffService staff, ICurrentClinicContext clinicContext) : base(clinicContext)
    {
        _staff = staff;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StaffMemberResponse>>> List(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();
        return Ok(await _staff.ListAsync(clinicId.Value, ct));
    }
}
