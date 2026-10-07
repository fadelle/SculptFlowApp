using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Requests.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>Clinics across the platform, for the admin portal.</summary>
public interface IClinicAdminService
{
    Task<PagedResponse<ClinicRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct);

    Task<List<ClinicOption>> OptionsAsync(CancellationToken ct);

    Task<ClinicDetail?> GetAsync(Guid id, CancellationToken ct);

    Task<ClinicCounts> CountsAsync(Guid id, CancellationToken ct);

    /// <summary>Throws KeyNotFoundException (no clinic) or ArgumentException (missing name, unknown time zone).</summary>
    Task<PlatformAdminChange> UpdateAsync(Guid id, ClinicUpdate update, CancellationToken ct);

    Task<PlatformAdminChange> SetActiveAsync(Guid id, bool active, CancellationToken ct);
}
