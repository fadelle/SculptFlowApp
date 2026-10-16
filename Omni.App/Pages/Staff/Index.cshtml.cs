using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Staff;
using Omni.App.Entities.Responses.Staff;

namespace Omni.App.Pages.Staff;

/// <summary>Staff: the users who have access to the current client (read-only list). The client always
/// comes from the logged-in user (ICurrentClientContext).</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IStaffService _staff;

    public IndexModel(ICurrentClientContext clientContext, IStaffService staff)
    {
        _clientContext = clientContext;
        _staff = staff;
    }

    public bool ClientConfigured { get; private set; }
    public string ClientName { get; private set; } = string.Empty;
    public string? CurrentUserId { get; private set; }
    public IReadOnlyList<StaffMemberResponse> Members { get; private set; } = Array.Empty<StaffMemberResponse>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return;

        ClientConfigured = true;
        ClientName = client.Name;
        CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Members = await _staff.ListAsync(client.Id, ct);
    }
}
