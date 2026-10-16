using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>Clients across the platform, for the admin portal.</summary>
public interface IClientAdminService
{
    Task<PagedResponse<ClientRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct);

    Task<List<ClientOption>> OptionsAsync(CancellationToken ct);

    Task<ClientDetail?> GetAsync(Guid id, CancellationToken ct);

    Task<ClientCounts> CountsAsync(Guid id, CancellationToken ct);

    /// <summary>Throws KeyNotFoundException (no client) or ArgumentException (missing name, unknown time zone).</summary>
    Task<PlatformAdminChange> UpdateAsync(Guid id, ClientUpdate update, CancellationToken ct);

    Task<PlatformAdminChange> SetActiveAsync(Guid id, bool active, CancellationToken ct);

    /// <summary>The client's AI bot switches and whether its plan includes the AI agent. Throws KeyNotFoundException.</summary>
    Task<InboxSettingsDto> GetInboxSettingsAsync(Guid id, CancellationToken ct);

    /// <summary>Saves the client's AI bot switches through IInboxSettingsService. Throws KeyNotFoundException.</summary>
    Task<PlatformAdminChange> UpdateInboxSettingsAsync(Guid id, UpdateInboxSettingsRequest request, CancellationToken ct);
}
