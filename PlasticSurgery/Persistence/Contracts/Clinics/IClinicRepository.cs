using PlasticSurgery.Entities.Dtos.Clinics;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Clinics;

/// <summary>Clinics and their staff memberships (clinic_users).</summary>
public interface IClinicRepository
{
    /// <summary>Tracked, so changes are written on the next save.</summary>
    Task<Clinic?> GetAsync(Guid clinicId, CancellationToken ct = default);

    Task<Clinic?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    void Add(Clinic clinic);

    void AddMembership(ClinicUser membership);

    /// <summary>The clinic of the user's active membership, or null.</summary>
    Task<Clinic?> GetClinicOfActiveMemberAsync(string userId, CancellationToken ct = default);

    Task<Guid?> GetClinicIdOfActiveMemberAsync(string userId, CancellationToken ct = default);

    /// <summary>Every membership of the clinic with the user's email and full-name claim.</summary>
    Task<IReadOnlyList<StaffMemberRow>> ListStaffAsync(Guid clinicId, string fullNameClaimType, CancellationToken ct = default);

    Task<IReadOnlyList<ClinicSummaryRow>> ListByNamePrefixAsync(string namePrefix, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListMemberUserIdsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Deletes the clinic row; the schema's ON DELETE CASCADE removes all of its data.</summary>
    Task DeleteAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Every clinic's id and name, by name.</summary>
    Task<IReadOnlyList<ClinicSummaryRow>> ListNamesAsync(CancellationToken ct = default);
}
