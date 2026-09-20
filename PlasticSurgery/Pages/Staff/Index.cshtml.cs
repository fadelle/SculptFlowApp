using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Staff;

/// <summary>Staff: the users who have access to the current clinic (read-only list). The clinic always
/// comes from the logged-in user (ICurrentClinicContext).</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IStaffService _staff;

    public IndexModel(ICurrentClinicContext clinicContext, IStaffService staff)
    {
        _clinicContext = clinicContext;
        _staff = staff;
    }

    public bool ClinicConfigured { get; private set; }
    public string ClinicName { get; private set; } = string.Empty;
    public string? CurrentUserId { get; private set; }
    public IReadOnlyList<StaffMemberResponse> Members { get; private set; } = Array.Empty<StaffMemberResponse>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return;

        ClinicConfigured = true;
        ClinicName = clinic.Name;
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Members = await _staff.ListAsync(clinic.Id, ct);
    }
}
