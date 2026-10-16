using Omni.App.Entities.Responses.Staff;

namespace Omni.App.Business.Contracts.Services.Staff;

/// <summary>Who has access to a client. Read-only for now: the list of users linked to the client through
/// client_users. Always scoped by the clientId the caller resolved from CurrentClientContext.</summary>
public interface IStaffService
{
    /// <summary>Members of this client only — active first, then by name/email. Full name comes from the
    /// Identity "full_name" claim set at registration (null for accounts created before that existed).</summary>
    Task<IReadOnlyList<StaffMemberResponse>> ListAsync(Guid clientId, CancellationToken ct = default);
}
