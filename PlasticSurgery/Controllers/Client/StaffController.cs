using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Staff;
using PlasticSurgery.Entities.Responses.Staff;

namespace PlasticSurgery.Controllers.Client;

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
