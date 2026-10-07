using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;
using PlasticSurgery.Persistence.Helpers;

namespace PlasticSurgery.Persistence.Repositories.PlatformAdmin;

public class LeadAdminRepository : ILeadAdminRepository
{
    private readonly ApplicationDbContext _db;

    public LeadAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clinicId, string? status, string? search, int page, CancellationToken ct = default)
    {
        var q = _db.Leads.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(l => l.ClinicId == clinicId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(l => (l.FullName != null && EF.Functions.ILike(l.FullName, like))
                             || (l.FirstName != null && EF.Functions.ILike(l.FirstName, like))
                             || (l.LastName != null && EF.Functions.ILike(l.LastName, like))
                             || (l.Phone != null && EF.Functions.ILike(l.Phone, like))
                             || (l.Email != null && EF.Functions.ILike(l.Email, like)));
        }
        return q.OrderByDescending(l => l.CreatedAt)
            .Select(l => new LeadRow(l.Id, l.ClinicId, l.Clinic!.Name,
                l.FullName ?? ((l.FirstName ?? "") + " " + (l.LastName ?? "")).Trim(),
                l.Phone, l.Email, l.Source, l.Status, l.QualificationStatus, l.Procedure != null ? l.Procedure.Name : null,
                l.MarketingOptIn, l.LastContactAt, l.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    public Task<Lead?> GetLeadAsync(Guid id, CancellationToken ct = default) =>
        _db.Leads.AsNoTracking().Include(l => l.Clinic).Include(l => l.Procedure).FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<List<EventLog>> LeadEventsAsync(Guid leadId, CancellationToken ct = default) =>
        _db.Events.AsNoTracking().Where(e => e.LeadId == leadId).OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync(ct);

    public Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clinicId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct = default)
    {
        var q = _db.Conversations.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(c => c.ClinicId == clinicId.Value);
        if (leadId.HasValue) q = q.Where(c => c.LeadId == leadId.Value);
        if (!string.IsNullOrWhiteSpace(channel)) q = q.Where(c => c.Channel == channel);
        if (!string.IsNullOrWhiteSpace(mode)) q = q.Where(c => c.Mode == mode);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
        return q.OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => new ConversationRow(c.Id, c.ClinicId,
                _db.Clinics.Where(x => x.Id == c.ClinicId).Select(x => x.Name).FirstOrDefault() ?? "",
                c.LeadId,
                c.Lead!.FullName ?? ((c.Lead.FirstName ?? "") + " " + (c.Lead.LastName ?? "")).Trim(),
                c.Channel, c.Status, c.Mode, c.LastMessageAt, c.LastMessageDirection,
                c.Messages.Count(), c.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    public Task<Conversation?> GetConversationAsync(Guid id, CancellationToken ct = default) =>
        _db.Conversations.AsNoTracking().Include(c => c.Lead).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Message>> MessagesAsync(Guid conversationId, int take = 500, CancellationToken ct = default) =>
        _db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt).Take(take).OrderBy(m => m.CreatedAt).ToListAsync(ct);

    public Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clinicId, bool failedOnly, string? sender, string? search,
        int page, CancellationToken ct = default)
    {
        var q = _db.Messages.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(m => m.ClinicId == clinicId.Value);
        if (failedOnly) q = q.Where(m => m.FailedAt != null || m.DeliveryStatus == "failed");
        if (!string.IsNullOrWhiteSpace(sender)) q = q.Where(m => m.SenderType == sender);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = QueryPaging.Like(search);
            q = q.Where(m => m.Content != null && EF.Functions.ILike(m.Content, like));
        }
        return q.OrderByDescending(m => m.CreatedAt)
            .Select(m => new MessageRow(m.Id, m.ClinicId,
                _db.Clinics.Where(x => x.Id == m.ClinicId).Select(x => x.Name).FirstOrDefault() ?? "",
                m.ConversationId, m.Channel, m.Direction, m.SenderType, m.Origin, m.Content, m.DeliveryStatus,
                m.FailureCode, m.FailureReason, m.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    public Task<PagedResponse<AppointmentRow>> ListAppointmentsAsync(Guid? clinicId, Guid? leadId, string? status, bool upcomingOnly,
        int page, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = _db.Appointments.AsNoTracking().AsQueryable();
        if (clinicId.HasValue) q = q.Where(a => a.ClinicId == clinicId.Value);
        if (leadId.HasValue) q = q.Where(a => a.LeadId == leadId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        q = upcomingOnly
            ? q.Where(a => a.ScheduledStart >= now).OrderBy(a => a.ScheduledStart)
            : q.OrderByDescending(a => a.ScheduledStart);
        return q.Select(a => new AppointmentRow(a.Id, a.ClinicId,
                _db.Clinics.Where(x => x.Id == a.ClinicId).Select(x => x.Name).FirstOrDefault() ?? "",
                _db.Clinics.Where(x => x.Id == a.ClinicId).Select(x => x.Timezone).FirstOrDefault() ?? "UTC",
                a.LeadId, a.Lead!.FullName ?? ((a.Lead.FirstName ?? "") + " " + (a.Lead.LastName ?? "")).Trim(),
                a.Procedure != null ? a.Procedure.Name : null, a.Status, a.ScheduledStart, a.ScheduledEnd, a.Notes, a.CreatedAt))
            .ToPagedAsync(page, ct: ct);
    }

    /// <summary>Tracked.</summary>
    public Task<Lead?> GetLeadForUpdateAsync(Guid id, CancellationToken ct = default) =>
        _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    /// <summary>Tracked.</summary>
    public Task<Conversation?> GetConversationForUpdateAsync(Guid id, CancellationToken ct = default) =>
        _db.Conversations.FirstOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>Tracked.</summary>
    public Task<Appointment?> GetAppointmentForUpdateAsync(Guid id, CancellationToken ct = default) =>
        _db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct);
}
