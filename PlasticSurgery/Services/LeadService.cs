using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class LeadService : ILeadService
{
    private readonly ApplicationDbContext _db;
    private readonly IEventLogger _events;
    private readonly IProcedureService _procedures;

    public LeadService(ApplicationDbContext db, IEventLogger events, IProcedureService procedures)
    {
        _db = db;
        _events = events;
        _procedures = procedures;
    }

    public async Task<(LeadResponse Lead, bool WasCreated)> CreateOrGetAsync(CreateLeadRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ExternalLeadId))
        {
            var existing = await _db.Leads
                .Include(l => l.Procedure)
                .FirstOrDefaultAsync(l => l.ClinicId == request.ClinicId && l.ExternalLeadId == request.ExternalLeadId, ct);

            if (existing is not null)
            {
                return (ToResponse(existing), false);
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

        _db.Leads.Add(lead);
        _events.Log(lead.ClinicId, EventTypes.LeadCreated, leadId: lead.Id, source: lead.Source);

        await _db.SaveChangesAsync(ct);

        if (lead.ProcedureId is not null)
        {
            await _db.Entry(lead).Reference(l => l.Procedure).LoadAsync(ct);
        }

        return (ToResponse(lead), true);
    }

    public async Task<LeadResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var lead = await _db.Leads
            .Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Id == id, ct);

        return lead is null ? null : ToResponse(lead);
    }

    public async Task<(IReadOnlyList<LeadResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, Guid? procedureId, string? search, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Leads.Include(l => l.Procedure).Where(l => l.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (procedureId.HasValue)
        {
            query = query.Where(l => l.ProcedureId == procedureId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.FullName ?? "", pattern) ||
                EF.Functions.ILike(l.Phone ?? "", pattern) ||
                EF.Functions.ILike(l.Email ?? "", pattern));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items.Select(ToResponse).ToList(), totalCount);
    }

    public async Task<LeadResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateLeadRequest request, CancellationToken ct = default)
    {
        var lead = await _db.Leads.Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Id == id, ct);

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
            await _db.Entry(lead).Reference(l => l.Procedure).LoadAsync(ct);
        }

        await _db.SaveChangesAsync(ct);
        return ToResponse(lead);
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

        var lead = await _db.Leads.Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Id == id, ct);

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

        await _db.SaveChangesAsync(ct);
        return ToResponse(lead);
    }

    public async Task<LeadResponse?> UpdateContextAsync(Guid clinicId, Guid id, UpdateLeadContextRequest request, CancellationToken ct = default)
    {
        if (request.QualificationStatus is not null && !LeadQualificationStatus.All.Contains(request.QualificationStatus))
        {
            throw new ArgumentException($"Invalid qualification status '{request.QualificationStatus}'.", nameof(request));
        }

        var lead = await _db.Leads.Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Id == id, ct);
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
            await _db.Entry(lead).Reference(l => l.Procedure).LoadAsync(ct);
        }

        await _db.SaveChangesAsync(ct);
        return ToResponse(lead);
    }

    public async Task<LeadResponse> GetOrCreateByPhoneAsync(Guid clinicId, string phone, string? fullName, CancellationToken ct = default)
    {
        var existing = await _db.Leads.Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.Phone == phone, ct);
        if (existing is not null) return ToResponse(existing);

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
        _db.Leads.Add(lead);
        _events.Log(clinicId, EventTypes.LeadCreated, leadId: lead.Id, source: "whatsapp");
        await _db.SaveChangesAsync(ct);

        return ToResponse(lead);
    }

    public async Task<LeadResponse> GetOrCreateByExternalIdAsync(
        Guid clinicId, string externalLeadId, string source, string? fullName, string? firstName, string? lastName,
        string? sourceDetail, CancellationToken ct = default)
    {
        var existing = await _db.Leads.Include(l => l.Procedure)
            .FirstOrDefaultAsync(l => l.ClinicId == clinicId && l.ExternalLeadId == externalLeadId, ct);
        if (existing is not null) return ToResponse(existing);

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
        _db.Leads.Add(lead);
        _events.Log(clinicId, EventTypes.LeadCreated, leadId: lead.Id, source: source);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A concurrent delivery created the same lead first — drop our pending lead/event and use theirs.
            _db.ChangeTracker.Clear();
            var winner = await _db.Leads.Include(l => l.Procedure)
                .FirstAsync(l => l.ClinicId == clinicId && l.ExternalLeadId == externalLeadId, ct);
            return ToResponse(winner);
        }

        return ToResponse(lead);
    }

    public async Task<IReadOnlyList<string>> GetDistinctSourcesAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.Leads
            .Where(l => l.ClinicId == clinicId && l.Source != null && l.Source != "")
            .Select(l => l.Source!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);

    private static LeadResponse ToResponse(Lead l) => new(
        l.Id, l.ClinicId, l.ProcedureId, l.Procedure?.Name,
        l.FullName, l.FirstName, l.LastName, l.Phone, l.Email,
        l.Source, l.SourceDetail, l.CampaignName, l.ExternalLeadId,
        l.Status, l.QualificationStatus, l.PreferredLanguage, l.City, l.DesiredTimeline, l.Notes,
        l.MarketingOptIn, l.OptedOutAt, l.LastContactAt, l.NextFollowupAt, l.CreatedAt, l.UpdatedAt
    );
}
