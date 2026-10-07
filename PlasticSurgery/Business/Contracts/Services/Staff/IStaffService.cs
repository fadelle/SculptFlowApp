using PlasticSurgery.Entities.Responses.Staff;

namespace PlasticSurgery.Business.Contracts.Services.Staff;

/// <summary>Who has access to a clinic. Read-only for now: the list of users linked to the clinic through
/// clinic_users. Always scoped by the clinicId the caller resolved from CurrentClinicContext.</summary>
public interface IStaffService
{
    /// <summary>Members of this clinic only — active first, then by name/email. Full name comes from the
    /// Identity "full_name" claim set at registration (null for accounts created before that existed).</summary>
    Task<IReadOnlyList<StaffMemberResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);
}
