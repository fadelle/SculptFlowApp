using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface ILeadService
{
    /// <summary>Creates a lead. If ExternalLeadId is set and a lead with the same
    /// (ClinicId, ExternalLeadId) already exists, returns the existing lead instead of
    /// creating a duplicate — this is what makes repeated webhook deliveries safe to retry.</summary>
    Task<(LeadResponse Lead, bool WasCreated)> CreateOrGetAsync(CreateLeadRequest request, CancellationToken ct = default);

    Task<LeadResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<LeadResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, Guid? procedureId, string? search, int skip, int take, CancellationToken ct = default);

    Task<LeadResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateLeadRequest request, CancellationToken ct = default);

    Task<LeadResponse?> UpdateStatusAsync(Guid clinicId, Guid id, UpdateLeadStatusRequest request, CancellationToken ct = default);

    /// <summary>Narrower update for the AI agent's update_lead tool — procedure interest, language,
    /// timeline, notes, follow-up date, and qualification status only. See UpdateLeadContextRequest
    /// for why identity fields are excluded.</summary>
    Task<LeadResponse?> UpdateContextAsync(Guid clinicId, Guid id, UpdateLeadContextRequest request, CancellationToken ct = default);

    /// <summary>Finds the lead already on file for this phone number (regardless of how/where they
    /// were first created — a website form, another channel, etc.), or creates a new one with
    /// Source="whatsapp". Used by the WhatsApp webhook processor for an inbound customer message,
    /// where only a phone number (no lead id) is ever available from Meta. Deliberately matches by
    /// Phone first rather than only an ExternalLeadId convention — a customer who already exists
    /// as a lead from a different source must not get a second, duplicate record just because they
    /// then messaged on WhatsApp.</summary>
    Task<LeadResponse> GetOrCreateByPhoneAsync(Guid clinicId, string phone, string? fullName, CancellationToken ct = default);

    /// <summary>Distinct non-empty Lead.Source values on file for this clinic, alphabetical — backs
    /// the "Lead source" filter dropdown on the Campaign creation page rather than a hardcoded list,
    /// since Source is a free string (see Lead.cs), not a fixed enum.</summary>
    Task<IReadOnlyList<string>> GetDistinctSourcesAsync(Guid clinicId, CancellationToken ct = default);
}
