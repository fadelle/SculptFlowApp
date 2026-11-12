using Omni.App.Entities.Responses.Billing;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Monitoring;

/// <summary>activity.platform_admin_writes: every write to the platform-admin API (QA t6-admin-23).</summary>
public interface IPlatformAdminWriteRepository
{
    /// <summary>Saves one row at once (its own save: the request's own changes are already committed or failed).</summary>
    Task AddAsync(PlatformAdminWrite write, CancellationToken ct = default);

    /// <summary>Newest first, 50 a page; optionally one client's.</summary>
    Task<PagedResponse<PlatformAdminWrite>> ListAsync(Guid? clientId, int page, CancellationToken ct = default);

    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
