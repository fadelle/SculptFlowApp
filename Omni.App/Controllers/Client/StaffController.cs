using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Staff;
using Omni.App.Entities.Responses.Staff;

namespace Omni.App.Controllers.Client;

/// <summary>The users of the logged-in user's client. Login required; the client always comes from
/// CurrentClientContext, never from the request.</summary>
[ApiController]
[Route("api/staff")]
public class StaffController : DashboardApiController
{
    private readonly IStaffService _staff;

    public StaffController(IStaffService staff, ICurrentClientContext clientContext) : base(clientContext)
    {
        _staff = staff;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StaffMemberResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _staff.ListAsync(clientId.Value, ct));
    }
}
