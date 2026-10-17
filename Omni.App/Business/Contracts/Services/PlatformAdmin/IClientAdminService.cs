using Omni.App.Entities.Dtos.BusinessTypes;
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

    /// <summary>The client's AI bot switches and whether its plan includes the AI assistant. Throws KeyNotFoundException.</summary>
    Task<InboxSettingsDto> GetInboxSettingsAsync(Guid id, CancellationToken ct);

    /// <summary>Saves the client's AI bot switches through IInboxSettingsService. Throws KeyNotFoundException.</summary>
    Task<PlatformAdminChange> UpdateInboxSettingsAsync(Guid id, UpdateInboxSettingsRequest request, CancellationToken ct);

    /// <summary>What moving the client to business type <paramref name="toCode"/> would do: contact counts per stage and
    /// where each group goes (IBusinessTypeService.PreviewClientChangeAsync).</summary>
    Task<BusinessTypeChangePreview> PreviewBusinessTypeChangeAsync(Guid id, string? toCode, CancellationToken ct);

    /// <summary>Moves the client to another business type and its contacts to the matching stages, in one transaction
    /// (IBusinessTypeService.ChangeClientTypeAsync).</summary>
    Task<PlatformAdminChange> ChangeBusinessTypeAsync(Guid id, string? toCode, CancellationToken ct);
}
