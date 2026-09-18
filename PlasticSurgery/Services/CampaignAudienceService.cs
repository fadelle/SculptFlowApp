using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class CampaignAudienceService : ICampaignAudienceService
{
    /// <summary>Appointment statuses that mean "this lead already reached a consultation" — excluded
    /// from reactivation. Canceled/NoShow do NOT count: a lead who cancelled or didn't show never
    /// actually had the consultation, so they remain eligible for reactivation. Rescheduled counts as
    /// booked (it's a live appointment moved to a new time, not an abandoned one).</summary>
    private static readonly string[] BookedOrCompletedAppointmentStatuses =
    {
        AppointmentStatus.Booked, AppointmentStatus.Confirmed, AppointmentStatus.Rescheduled, AppointmentStatus.Attended
    };

    /// <summary>Default lookback when a campaign's filters don't specify inactiveDays — matches the
    /// "60 days" example in the reactivation business rule.</summary>
    private const int DefaultInactiveDays = 60;

    private readonly ApplicationDbContext _db;

    public CampaignAudienceService(ApplicationDbContext db)
    {
        _db = db;
    }

    public string? GetSkipReasonIfNotContactable(Lead lead)
    {
        if (string.IsNullOrWhiteSpace(lead.Phone)) return "no_phone";
        if (lead.OptedOutAt.HasValue) return "opted_out";
        if (!lead.MarketingOptIn) return "marketing_opt_in_false";
        return null;
    }

    public async Task<List<Lead>> GetEligibleLeadsAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default)
    {
        var query = BuildQuery(clinicId, audienceType, CampaignAudienceFilters.Parse(filtersJson));
        return await query.ToListAsync(ct);
    }

    public async Task<int> GetMatchingCountAsync(Guid clinicId, string audienceType, string? filtersJson, CancellationToken ct = default)
    {
        var query = BuildQuery(clinicId, audienceType, CampaignAudienceFilters.Parse(filtersJson));
        return await query.CountAsync(ct);
    }

    /// <summary>Mandatory exclusions (always) + audience-type-specific rules. This is the ONLY place
    /// eligibility is computed — "all_eligible" is deliberately not "every lead row", it's this same
    /// mandatory-exclusion base with no further narrowing.</summary>
    private IQueryable<Lead> BuildQuery(Guid clinicId, string audienceType, CampaignAudienceFilters filters)
    {
        var contactable = _db.Leads.Where(l =>
            l.ClinicId == clinicId &&
            l.Phone != null && l.Phone != "" &&
            l.MarketingOptIn &&
            l.OptedOutAt == null);

        return audienceType switch
        {
            CampaignAudienceType.AllEligible => contactable,
            CampaignAudienceType.ReactivationNoConsultation => ApplyReactivationRules(contactable, filters),
            CampaignAudienceType.Custom => ApplyCustomFilters(contactable, filters),
            _ => contactable.Where(_ => false)
        };
    }

    /// <summary>"Old lead reactivation" — prior interest/activity, inactive for N days, and never
    /// reached a booked/confirmed/completed consultation. Nothing here reads a stored flag; every
    /// condition is derived live from leads/conversations/appointments (see class remarks in
    /// ICampaignAudienceService).</summary>
    private static IQueryable<Lead> ApplyReactivationRules(IQueryable<Lead> query, CampaignAudienceFilters filters)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-(filters.InactiveDays ?? DefaultInactiveDays));

        query = query.Where(l =>
            // Prior interest/activity: an actual conversation happened, or the lead was manually
            // marked past "new" (e.g. contacted, qualified) even without a logged conversation.
            (l.Conversations.Any() || l.Status != LeadStatus.New) &&
            // Inactive: no logged contact and no conversation activity more recent than the cutoff.
            (l.LastContactAt == null || l.LastContactAt <= cutoff) &&
            !l.Conversations.Any(c => c.LastMessageAt != null && c.LastMessageAt > cutoff) &&
            // Never reached a successfully booked/completed consultation.
            !l.Appointments.Any(a => BookedOrCompletedAppointmentStatuses.Contains(a.Status)));

        return ApplyOptionalScopingFilters(query, filters);
    }

    /// <summary>Clinic-defined filters — no "never booked" requirement, just whichever optional
    /// fields the clinic set. Common ones (leadStatuses, sources, procedureId, inactiveDays) are
    /// shared with reactivation; everything past that (qualification, dates, appointment status,
    /// country/city/language) only ever applies here — the custom-audience "Advanced filters"
    /// section in the Campaign creation UI.</summary>
    private static IQueryable<Lead> ApplyCustomFilters(IQueryable<Lead> query, CampaignAudienceFilters filters)
    {
        if (filters.InactiveDays.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-filters.InactiveDays.Value);
            query = query.Where(l =>
                (l.LastContactAt == null || l.LastContactAt <= cutoff) &&
                !l.Conversations.Any(c => c.LastMessageAt != null && c.LastMessageAt > cutoff));
        }

        query = ApplyOptionalScopingFilters(query, filters);

        if (filters.QualificationStatuses is { Count: > 0 })
        {
            query = query.Where(l => filters.QualificationStatuses.Contains(l.QualificationStatus));
        }
        if (filters.CreatedAfter.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= filters.CreatedAfter.Value);
        }
        if (filters.CreatedBefore.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= filters.CreatedBefore.Value);
        }
        if (filters.LastContactedAfter.HasValue)
        {
            query = query.Where(l => l.LastContactAt != null && l.LastContactAt >= filters.LastContactedAfter.Value);
        }
        if (filters.LastContactedBefore.HasValue)
        {
            query = query.Where(l => l.LastContactAt != null && l.LastContactAt <= filters.LastContactedBefore.Value);
        }
        if (filters.AppointmentStatuses is { Count: > 0 })
        {
            query = query.Where(l => l.Appointments.Any(a => filters.AppointmentStatuses.Contains(a.Status)));
        }
        if (filters.Countries is { Count: > 0 })
        {
            query = query.Where(l => l.CountryCode != null && filters.Countries.Contains(l.CountryCode));
        }
        if (filters.Cities is { Count: > 0 })
        {
            query = query.Where(l => l.City != null && filters.Cities.Contains(l.City));
        }
        return query;
    }

    private static IQueryable<Lead> ApplyOptionalScopingFilters(IQueryable<Lead> query, CampaignAudienceFilters filters)
    {
        if (filters.ProcedureId.HasValue)
        {
            query = query.Where(l => l.ProcedureId == filters.ProcedureId.Value);
        }
        if (filters.LeadStatuses is { Count: > 0 })
        {
            query = query.Where(l => filters.LeadStatuses.Contains(l.Status));
        }
        if (filters.Sources is { Count: > 0 })
        {
            query = query.Where(l => l.Source != null && filters.Sources.Contains(l.Source));
        }
        return query;
    }
}
