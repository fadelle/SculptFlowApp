using Omni.App.Entities.Dtos.PlatformAdmin;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

/// <summary>Reads over admin users (identity.users with role admin). Single users are read and written through
/// Identity's UserManager.</summary>
public interface IAdminUserRepository
{
    /// <summary>Every admin, active first, then by email.</summary>
    Task<IReadOnlyList<AdminRow>> ListAsync(CancellationToken ct = default);

    Task<int> CountActiveAsync(CancellationToken ct = default);
}
