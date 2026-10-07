using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Leads;

/// <summary>Leads. Methods that return a lead return it tracked (changes are written on the next save) with its
/// Procedure loaded, because every caller shows the procedure name.</summary>
public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid clinicId, Guid leadId, CancellationToken ct = default);

    /// <summary>Tracked, without the procedure; for internal updates by id alone.</summary>
    Task<Lead?> GetByIdAsync(Guid leadId, CancellationToken ct = default);

    Task<Lead?> FindByExternalIdAsync(Guid clinicId, string externalLeadId, CancellationToken ct = default);

    Task<Lead?> FindByPhoneAsync(Guid clinicId, string phone, CancellationToken ct = default);

    /// <summary>Newest first. <paramref name="search"/> matches name, phone or email (case-insensitive, contains).</summary>
    Task<(IReadOnlyList<Lead> Items, int TotalCount)> ListAsync(Guid clinicId, string? status, Guid? procedureId,
        string? search, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDistinctSourcesAsync(Guid clinicId, CancellationToken ct = default);

    void Add(Lead lead);

    /// <summary>(Re)loads lead.Procedure after its ProcedureId was set or changed.</summary>
    Task LoadProcedureAsync(Lead lead, CancellationToken ct = default);

    /// <summary>Tracked, without the procedure.</summary>
    Task<List<Lead>> ListByIdsAsync(Guid clinicId, IReadOnlyCollection<Guid> leadIds, CancellationToken ct = default);

    Task<string?> GetFullNameAsync(Guid leadId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid clinicId, Guid leadId, CancellationToken ct = default);
}
