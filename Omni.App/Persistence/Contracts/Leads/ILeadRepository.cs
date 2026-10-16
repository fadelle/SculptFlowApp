using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Leads;

/// <summary>Leads. Methods that return a lead return it tracked (changes are written on the next save) with its
/// Service loaded, because every caller shows the service name.</summary>
public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid clientId, Guid leadId, CancellationToken ct = default);

    /// <summary>Tracked, without the service; for internal updates by id alone.</summary>
    Task<Lead?> GetByIdAsync(Guid leadId, CancellationToken ct = default);

    Task<Lead?> FindByExternalIdAsync(Guid clientId, string externalLeadId, CancellationToken ct = default);

    Task<Lead?> FindByPhoneAsync(Guid clientId, string phone, CancellationToken ct = default);

    /// <summary>Newest first. <paramref name="search"/> matches name, phone or email (case-insensitive, contains).</summary>
    Task<(IReadOnlyList<Lead> Items, int TotalCount)> ListAsync(Guid clientId, string? status, Guid? serviceId,
        string? search, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDistinctSourcesAsync(Guid clientId, CancellationToken ct = default);

    void Add(Lead lead);

    /// <summary>(Re)loads lead.Service after its ServiceId was set or changed.</summary>
    Task LoadServiceAsync(Lead lead, CancellationToken ct = default);

    /// <summary>Tracked, without the service.</summary>
    Task<List<Lead>> ListByIdsAsync(Guid clientId, IReadOnlyCollection<Guid> leadIds, CancellationToken ct = default);

    Task<string?> GetFullNameAsync(Guid leadId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clientId, Guid leadId, CancellationToken ct = default);

    /// <summary>How many of the client's leads are in each status (stage key); statuses nobody is in are left out.</summary>
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Set-based: moves every lead of the client in <paramref name="fromStatus"/> to <paramref name="toStatus"/>
    /// (a business type change). Returns how many moved.</summary>
    Task<int> MoveStatusAsync(Guid clientId, string fromStatus, string toStatus, DateTimeOffset now, CancellationToken ct = default);
}
