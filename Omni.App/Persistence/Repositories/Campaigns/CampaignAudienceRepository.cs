using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Campaigns;

namespace Omni.App.Persistence.Repositories.Campaigns;

public class CampaignAudienceRepository : ICampaignAudienceRepository
{
    /// <summary>Booking statuses that mean "this lead already reached a booking" — excluded
    /// from reactivation. Canceled/NoShow do NOT count: a lead who cancelled or didn't show never
    /// actually had the booking, so they remain eligible for reactivation. Rescheduled counts as
    /// booked (it's a live booking moved to a new time, not an abandoned one).</summary>
    private static readonly string[] BookedOrCompletedBookingStatuses =
    {
        BookingStatus.Booked, BookingStatus.Confirmed, BookingStatus.Rescheduled, BookingStatus.Attended
    };

    private readonly ApplicationDbContext _db;

    public CampaignAudienceRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<List<Lead>> ListEligibleLeadsAsync(Guid clientId, string audienceType, CampaignAudienceFilters filters,
        int defaultInactiveDays, CancellationToken ct = default) =>
        BuildQuery(clientId, audienceType, filters, defaultInactiveDays).ToListAsync(ct);

    public Task<int> CountEligibleLeadsAsync(Guid clientId, string audienceType, CampaignAudienceFilters filters,
        int defaultInactiveDays, CancellationToken ct = default) =>
        BuildQuery(clientId, audienceType, filters, defaultInactiveDays).CountAsync(ct);

    /// <summary>Mandatory exclusions (always) + audience-type-specific rules. This is the ONLY place
    /// eligibility is computed — "all_eligible" is deliberately not "every lead row", it's this same
    /// mandatory-exclusion base with no further narrowing.</summary>
    private IQueryable<Lead> BuildQuery(Guid clientId, string audienceType, CampaignAudienceFilters filters, int defaultInactiveDays)
    {
        var contactable = _db.Leads.Where(l =>
            l.ClientId == clientId &&
            l.Phone != null && l.Phone != "" &&
            l.MarketingOptIn &&
            l.OptedOutAt == null);

        return audienceType switch
        {
            CampaignAudienceType.AllEligible => contactable,
            CampaignAudienceType.ReactivationNoBooking => ApplyReactivationRules(contactable, filters, defaultInactiveDays),
            CampaignAudienceType.Custom => ApplyCustomFilters(contactable, filters),
            _ => contactable.Where(_ => false)
        };
    }

    /// <summary>"Old lead reactivation" — prior interest/activity, inactive for N days, and never
    /// reached a booked/confirmed/completed booking. Nothing here reads a stored flag; every
    /// condition is derived live from leads/conversations/bookings (see class remarks in
    /// ICampaignAudienceService).</summary>
    private static IQueryable<Lead> ApplyReactivationRules(IQueryable<Lead> query, CampaignAudienceFilters filters, int defaultInactiveDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-(filters.InactiveDays ?? defaultInactiveDays));

        query = query.Where(l =>
            // Prior interest/activity: an actual conversation happened, or the lead was manually
            // marked past "new" (e.g. contacted, qualified) even without a logged conversation.
            (l.Conversations.Any() || l.Status != LeadStatus.New) &&
            // Inactive: no logged contact and no conversation activity more recent than the cutoff.
            (l.LastContactAt == null || l.LastContactAt <= cutoff) &&
            !l.Conversations.Any(c => c.LastMessageAt != null && c.LastMessageAt > cutoff) &&
            // Never reached a successfully booked/completed booking.
            !l.Bookings.Any(a => BookedOrCompletedBookingStatuses.Contains(a.Status)));

        return ApplyOptionalScopingFilters(query, filters);
    }

    /// <summary>Client-defined filters — no "never booked" requirement, just whichever optional
    /// fields the client set. Common ones (leadStatuses, sources, serviceId, inactiveDays) are
    /// shared with reactivation; everything past that (qualification, dates, booking status,
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
        // Npgsql only accepts UTC offsets for timestamptz; filters may arrive with any offset.
        var createdAfter = filters.CreatedAfter?.ToUniversalTime();
        var createdBefore = filters.CreatedBefore?.ToUniversalTime();
        var lastContactedAfter = filters.LastContactedAfter?.ToUniversalTime();
        var lastContactedBefore = filters.LastContactedBefore?.ToUniversalTime();
        if (createdAfter.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= createdAfter.Value);
        }
        if (createdBefore.HasValue)
        {
            query = query.Where(l => l.CreatedAt < createdBefore.Value);
        }
        if (lastContactedAfter.HasValue)
        {
            query = query.Where(l => l.LastContactAt != null && l.LastContactAt >= lastContactedAfter.Value);
        }
        if (lastContactedBefore.HasValue)
        {
            query = query.Where(l => l.LastContactAt != null && l.LastContactAt < lastContactedBefore.Value);
        }
        if (filters.BookingStatuses is { Count: > 0 })
        {
            query = query.Where(l => l.Bookings.Any(a => filters.BookingStatuses.Contains(a.Status)));
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
        if (filters.ServiceId.HasValue)
        {
            query = query.Where(l => l.ServiceId == filters.ServiceId.Value);
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
