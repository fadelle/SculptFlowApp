using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface IClientAdminRepository
{
    Task<PagedResponse<ClientRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct = default);

    /// <summary>Every client as (id, name), for the client filter on list pages.</summary>
    Task<List<ClientOption>> OptionsAsync(CancellationToken ct = default);

    Task<Client?> GetAsync(Guid id, CancellationToken ct = default);

    Task<ClientCounts> CountsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Client?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
}
