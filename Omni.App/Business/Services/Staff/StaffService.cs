using Omni.App.Business.Contracts.Services.Staff;
using Omni.App.Business.Services.Clients;
using Omni.App.Entities.Responses.Staff;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Staff;

public class StaffService : IStaffService
{
    private readonly IClientRepository _clients;

    public StaffService(IClientRepository clients)
    {
        _clients = clients;
    }

    public async Task<IReadOnlyList<StaffMemberResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var rows = await _clients.ListStaffAsync(clientId, ClientRegistrationService.FullNameClaimType, ct);

        return rows
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.FullName ?? r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StaffMemberResponse(r.UserId, r.FullName, r.Email, r.IsActive, r.CreatedAt))
            .ToList();
    }
}
