using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Leads;
using PlasticSurgery.Business.Contracts.Services.Procedures;
using PlasticSurgery.Business.Mappers.Leads;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Leads;
using PlasticSurgery.Entities.Responses.Leads;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Leads;

namespace PlasticSurgery.Business.Services.Leads;

public class LeadService : ILeadService
{
    private readonly ILeadRepository _leads;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly IProcedureService _procedures;

    public LeadService(ILeadRepository leads, IUnitOfWork unitOfWork, IEventLogger events, IProcedureService procedures)
    {
        _leads = leads;
        _unitOfWork = unitOfWork;
        _events = events;
        _procedures = procedures;
    }

    public async Task<(LeadResponse Lead, bool WasCreated)> CreateOrGetAsync(CreateLeadRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ExternalLeadId))
        {
            var existing = await _leads.FindByExternalIdAsync(request.ClinicId, request.ExternalLeadId, ct);

            if (existing is not null)
            {
                return (LeadMapper.ToResponse(existing), false);
            }
        }

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            ProcedureId = request.ProcedureId,
            FullName = request.FullName,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Email = request.Email,
            Source = request.Source,
            SourceDetail = request.SourceDetail,
            CampaignName = request.CampaignName,
            ExternalLeadId = request.ExternalLeadId,
            PreferredLanguage = request.PreferredLanguage,
            City = request.City,
            DesiredTimeline = request.DesiredTimeline,
            Notes = request.Notes,
            MarketingOptIn = request.MarketingOptIn,
            Status = LeadStatus.New,
            QualificationStatus = LeadQualificationStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _leads.Add(lead);
        _events.Log(lead.ClinicId, EventTypes.LeadCreated, leadId: lead.Id, source: lead.Source);

        await _unitOfWork.SaveChangesAsync(ct);

        if (lead.ProcedureId is not null)
        {
            await _leads.LoadProcedureAsync(lead, ct);
        }

        return (LeadMapper.ToResponse(lead), true);
    }

    public async Task<LeadResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var lead = await _leads.GetAsync(clinicId, id, ct);

        return lead is null ? null : LeadMapper.ToResponse(lead);
    }

    public async Task<(IReadOnlyList<LeadResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, Guid? procedureId, string? search, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _leads.ListAsync(clinicId, status, procedureId, search, skip, take, ct);
        return (items.Select(LeadMapper.ToResponse).ToList(), totalCount);
    }

    public async Task<LeadResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateLeadRequest request, CancellationToken ct = default)
    {
        var lead = await _leads.GetAsync(clinicId, id, ct);

        if (lead is null) return null;

        // Only a CHANGE of procedure interest is validated — resubmitting the lead's current
        // (possibly now-inactive) procedure must keep working.
        if (request.ProcedureId is { } newProcedureId && newProcedureId != lead.ProcedureId)
        {
            await _procedures.EnsureUsableAsync(clinicId, newProcedureId, ct);
        }

        if (request.ProcedureId is not null) lead.ProcedureId = request.ProcedureId;
        if (request.FullName is not null) lead.FullName = request.FullName;
        if (request.FirstName is not null) lead.FirstName = request.FirstName;
        if (request.LastName is not null) lead.LastName = request.LastName;
        if (request.Phone is not null) lead.Phone = request.Phone;
        if (request.Email is not null) lead.Email = request.Email;
        if (request.PreferredLanguage is not null) lead.PreferredLanguage = request.PreferredLanguage;
        if (request.City is not null) lead.City = request.City;
        if (request.DesiredTimeline is not null) lead.DesiredTimeline = request.DesiredTimeline;
        if (request.Notes is not null) lead.Notes = request.Notes;
        if (request.NextFollowupAt is not null) lead.NextFollowupAt = request.NextFollowupAt;

        lead.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.ProcedureId is not null)
        {
            await _leads.LoadProcedureAsync(lead, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return LeadMapper.ToResponse(lead);
    }

    public async Task<LeadResponse?> UpdateStatusAsync(Guid clinicId, Guid id, UpdateLeadStatusRequest request, CancellationToken ct = default)
    {
        if (!LeadStatus.All.Contains(request.Status))
        {
            throw new ArgumentException($"Invalid lead status '{request.Status}'.", nameof(request));
        }

        if (request.QualificationStatus is not null && !LeadQualificationStatus.All.Contains(request.QualificationStatus))
        {
            throw new ArgumentException($"Invalid qualification status '{request.QualificationStatus}'.", nameof(request));
        }

        var lead = await _leads.GetAsync(clinicId, id, ct);

        if (lead is null) return null;

        var previousStatus = lead.Status;
        lead.Status = request.Status;
        if (request.QualificationStatus is not null)
        {
            lead.QualificationStatus = request.QualificationStatus;
        }
        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            lead.Source = request.Source;
        }

        lead.UpdatedAt = DateTimeOffset.UtcNow;

        if (previousStatus != request.Status)
        {
            // Only a real status transition counts as "contact" — a staff correction to
            // qualification/source alone (status resubmitted unchanged) must not reset
            // LastContactAt, since the reactivation audience (CampaignAudienceService) uses it as
            // the inactivity clock; a metadata-only edit shouldn't accidentally pull a lead out of
            // that audience.
            lead.LastContactAt = DateTimeOffset.UtcNow;
            var eventType = request.Status switch
            {
                LeadStatus.Qualified => EventTypes.LeadQualified,
                LeadStatus.ConsultationBooked => EventTypes.ConsultationBooked,
                LeadStatus.ConsultationAttended => EventTypes.ConsultationAttended,
                LeadStatus.NoShow => EventTypes.ConsultationNoShow,
                LeadStatus.NeedsHuman => EventTypes.HumanHandoff,
                _ => EventTypes.LeadContacted
            };
            _events.Log(lead.ClinicId, eventType, leadId: lead.Id);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return LeadMapper.ToResponse(lead);
    }

    public async Task<LeadResponse?> UpdateContextAsync(Guid clinicId, Guid id, UpdateLeadContextRequest request, CancellationToken ct = default)
    {
        if (request.QualificationStatus is not null && !LeadQualificationStatus.All.Contains(request.QualificationStatus))
        {
            throw new ArgumentException($"Invalid qualification status '{request.QualificationStatus}'.", nameof(request));
        }

        var lead = await _leads.GetAsync(clinicId, id, ct);
        if (lead is null) return null;

        if (request.ProcedureId is { } newProcedureId && newProcedureId != lead.ProcedureId)
        {
            await _procedures.EnsureUsableAsync(clinicId, newProcedureId, ct);
        }

        if (request.ProcedureId is not null) lead.ProcedureId = request.ProcedureId;
        if (request.PreferredLanguage is not null) lead.PreferredLanguage = request.PreferredLanguage;
        if (request.City is not null) lead.City = request.City;
        if (request.DesiredTimeline is not null) lead.DesiredTimeline = request.DesiredTimeline;
        if (request.Notes is not null) lead.Notes = request.Notes;
        if (request.NextFollowupAt is not null) lead.NextFollowupAt = request.NextFollowupAt;
        if (request.QualificationStatus is not null) lead.QualificationStatus = request.QualificationStatus;

        lead.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.ProcedureId is not null)
        {
            await _leads.LoadProcedureAsync(lead, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return LeadMapper.ToResponse(lead);
    }

    public async Task<(LeadResponse Lead, bool WasCreated)> GetOrCreateByPhoneAsync(Guid clinicId, string phone, string? fullName, CancellationToken ct = default)
    {
        var existing = await _leads.FindByPhoneAsync(clinicId, phone, ct);
        if (existing is not null) return (LeadMapper.ToResponse(existing), false);

        var now = DateTimeOffset.UtcNow;
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            FullName = fullName,
            Phone = phone,
            Source = "whatsapp",
            ExternalLeadId = $"whatsapp:{phone}",
            Status = LeadStatus.New,
            QualificationStatus = LeadQualificationStatus.Unknown,
            MarketingOptIn = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _leads.Add(lead);
        _events.Log(clinicId, EventTypes.LeadCreated, leadId: lead.Id, source: "whatsapp");
        await _unitOfWork.SaveChangesAsync(ct);

        return (LeadMapper.ToResponse(lead), true);
    }

    public async Task<(LeadResponse Lead, bool WasCreated)> GetOrCreateByExternalIdAsync(
        Guid clinicId, string externalLeadId, string source, string? fullName, string? firstName, string? lastName,
        string? sourceDetail, CancellationToken ct = default)
    {
        var existing = await _leads.FindByExternalIdAsync(clinicId, externalLeadId, ct);
        if (existing is not null) return (LeadMapper.ToResponse(existing), false);

        var now = DateTimeOffset.UtcNow;
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            FullName = fullName,
            FirstName = firstName,
            LastName = lastName,
            Phone = null,
            Source = source,
            SourceDetail = sourceDetail,
            ExternalLeadId = externalLeadId,
            Status = LeadStatus.New,
            QualificationStatus = LeadQualificationStatus.Unknown,
            MarketingOptIn = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _leads.Add(lead);
        _events.Log(clinicId, EventTypes.LeadCreated, leadId: lead.Id, source: source);

        if (!await _unitOfWork.TrySaveChangesAsync(ct))
        {
            // A concurrent delivery created the same lead first — our pending lead/event were dropped; use theirs.
            var winner = await _leads.FindByExternalIdAsync(clinicId, externalLeadId, ct)
                ?? throw new InvalidOperationException("Lead conflict reported but the existing lead was not found.");
            return (LeadMapper.ToResponse(winner), false); // a concurrent delivery created it first — not "our" new lead
        }

        return (LeadMapper.ToResponse(lead), true);
    }

    public Task<IReadOnlyList<string>> GetDistinctSourcesAsync(Guid clinicId, CancellationToken ct = default) =>
        _leads.ListDistinctSourcesAsync(clinicId, ct);
}
