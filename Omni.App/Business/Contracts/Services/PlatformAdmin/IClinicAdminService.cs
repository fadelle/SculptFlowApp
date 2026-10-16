using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

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

    /// <summary>The clinic's AI bot switches and whether its plan includes the AI agent. Throws KeyNotFoundException.</summary>
    Task<InboxSettingsDto> GetInboxSettingsAsync(Guid id, CancellationToken ct);

    /// <summary>Saves the clinic's AI bot switches through IInboxSettingsService. Throws KeyNotFoundException.</summary>
    Task<PlatformAdminChange> UpdateInboxSettingsAsync(Guid id, UpdateInboxSettingsRequest request, CancellationToken ct);
}
