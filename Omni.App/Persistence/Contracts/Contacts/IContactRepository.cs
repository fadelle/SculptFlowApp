using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Contacts;

/// <summary>Contacts. Methods that return a contact return it tracked (changes are written on the next save) with its
/// Service loaded, because every caller shows the service name.</summary>
public interface IContactRepository
{
    Task<Contact?> GetAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>Tracked, without the service; for internal updates by id alone.</summary>
    Task<Contact?> GetByIdAsync(Guid contactId, CancellationToken ct = default);

    Task<Contact?> FindByExternalIdAsync(Guid clientId, string externalContactId, CancellationToken ct = default);

    Task<Contact?> FindByPhoneAsync(Guid clientId, string phone, CancellationToken ct = default);

    /// <summary>Newest first. <paramref name="search"/> matches name, phone or email (case-insensitive, contains).</summary>
    Task<(IReadOnlyList<Contact> Items, int TotalCount)> ListAsync(Guid clientId, string? stage, Guid? serviceId,
        string? search, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDistinctSourcesAsync(Guid clientId, CancellationToken ct = default);

    void Add(Contact contact);

    /// <summary>(Re)loads contact.Service after its ServiceId was set or changed.</summary>
    Task LoadServiceAsync(Contact contact, CancellationToken ct = default);

    /// <summary>Tracked, without the service.</summary>
    Task<List<Contact>> ListByIdsAsync(Guid clientId, IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default);

    Task<string?> GetFullNameAsync(Guid contactId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>How many of the client's contacts are in each stage key; keys nobody is in are left out.</summary>
    Task<IReadOnlyDictionary<string, int>> CountByStageAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Set-based: moves every contact of the client in <paramref name="fromKey"/> to <paramref name="toKey"/>
    /// (a business type change). Returns how many moved.</summary>
    Task<int> MoveStageAsync(Guid clientId, string fromKey, string toKey, DateTimeOffset now, CancellationToken ct = default);
}
