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

    /// <summary>The client's connected accounts with their AI settings, its bots, and what its plan allows. Throws
    /// KeyNotFoundException.</summary>
    Task<InboxSettingsDto> GetInboxSettingsAsync(Guid id, CancellationToken ct);

    /// <summary>Saves one account's AI settings through IInboxSettingsService (its rules: an active bot of the client's, the
    /// plan's AI assistant). Throws KeyNotFoundException for an unknown client or account.</summary>
    Task<PlatformAdminChange> UpdateAccountAiAsync(Guid id, Guid channelIntegrationId, UpdateAccountAiRequest request, CancellationToken ct);

    /// <summary>The language the client's dashboard opens in for users without their own (en, ar, fr), through
    /// IClientProfileService. Throws KeyNotFoundException, ArgumentException for an unknown language.</summary>
    Task<PlatformAdminChange> SetDefaultLanguageAsync(Guid id, string? language, CancellationToken ct);

    /// <summary>What moving the client to business type <paramref name="toCode"/> would do: contact counts per stage and
    /// where each group goes (IBusinessTypeService.PreviewClientChangeAsync).</summary>
    Task<BusinessTypeChangePreview> PreviewBusinessTypeChangeAsync(Guid id, string? toCode, CancellationToken ct);

    /// <summary>Moves the client to another business type and its contacts to the matching stages, in one transaction
    /// (IBusinessTypeService.ChangeClientTypeAsync).</summary>
    Task<PlatformAdminChange> ChangeBusinessTypeAsync(Guid id, string? toCode, CancellationToken ct);
}
