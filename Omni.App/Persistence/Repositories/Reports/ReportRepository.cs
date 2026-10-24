using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Reports;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Reports;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories.Reports;

public class ReportRepository : IReportRepository
{
    /// <summary>Outbound messages that answer a customer: the AI, the Inbox, or the WhatsApp Business app (coexistence echo).
    /// Campaign and system messages are not replies.</summary>
    private static readonly string[] ReplyOrigins = [MessageOrigin.Ai, MessageOrigin.Dashboard, MessageOrigin.WhatsAppBusinessApp];

    private readonly ApplicationDbContext _db;

    public ReportRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ReportChannelRow>> GetChannelActivityAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var conversations = await _db.Conversations.AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .InRange(c => c.CreatedAt, from, to)
            .GroupBy(c => c.Channel)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var messages = await _db.Messages.AsNoTracking()
            .Where(m => m.ClientId == clientId)
            .InRange(m => m.CreatedAt, from, to)
            .GroupBy(m => m.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                Received = g.Count(m => m.Direction == MessageDirection.Inbound),
                Sent = g.Count(m => m.Direction == MessageDirection.Outbound),
            })
            .ToListAsync(ct);

        return conversations.Select(c => c.Channel).Union(messages.Select(m => m.Channel))
            .Select(channel =>
            {
                var m = messages.FirstOrDefault(x => x.Channel == channel);
                return new ReportChannelRow(channel, conversations.FirstOrDefault(x => x.Channel == channel)?.Count ?? 0,
                    m?.Received ?? 0, m?.Sent ?? 0);
            })
            .OrderByDescending(r => r.Received + r.Sent).ThenByDescending(r => r.NewConversations).ThenBy(r => r.Channel, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<ReportCountRow>> CountNewContactsBySourceAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default) =>
        await _db.Contacts.AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .InRange(c => c.CreatedAt, from, to)
            .GroupBy(c => c.Source)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count).ThenBy(r => r.Key)
            .Select(r => new ReportCountRow(r.Key, r.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReportCountRow>> CountNewContactsByStageAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default) =>
        await _db.Contacts.AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .InRange(c => c.CreatedAt, from, to)
            .GroupBy(c => c.StageKey)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count).ThenBy(r => r.Key)
            .Select(r => new ReportCountRow(r.Key, r.Count))
            .ToListAsync(ct);

    public async Task<ReportBookingCounts> GetBookingCountsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var bookings = _db.Bookings.AsNoTracking().Where(b => b.ClientId == clientId);
        var made = await bookings.InRange(b => b.CreatedAt, from, to).CountAsync(ct);
        var outcomes = await bookings.InRange(b => b.ScheduledStart, from, to)
            .Where(b => b.Status == BookingStatus.Attended || b.Status == BookingStatus.NoShow || b.Status == BookingStatus.Canceled)
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(string status) => outcomes.FirstOrDefault(o => o.Status == status)?.Count ?? 0;
        return new ReportBookingCounts(made, Count(BookingStatus.Attended), Count(BookingStatus.NoShow), Count(BookingStatus.Canceled));
    }

    public async Task<IReadOnlyList<ReportCampaignRow>> ListCampaignsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to, int take,
        CancellationToken ct = default) =>
        await _db.Campaigns.AsNoTracking()
            .Where(c => c.ClientId == clientId && c.StartedAt != null)
            .InRange(c => c.StartedAt!.Value, from, to)
            .OrderByDescending(c => c.StartedAt)
            .Take(take)
            .Select(c => new ReportCampaignRow(
                c.Id, c.Name, c.StartedAt,
                c.Recipients.Count(r => r.Status != CampaignRecipientStatus.Skipped),
                c.Recipients.Count(r => r.SentAt != null),
                c.Recipients.Count(r => r.DeliveredAt != null || r.ReadAt != null),
                c.Recipients.Count(r => r.ReadAt != null),
                c.Recipients.Count(r => r.RepliedAt != null),
                c.Recipients.Count(r => r.Status == CampaignRecipientStatus.Failed)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReportWait>> ListWaitsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var messages = _db.Messages.AsNoTracking().Where(m => m.ClientId == clientId);
        var replies = messages.Where(m => m.Direction == MessageDirection.Outbound && ReplyOrigins.Contains(m.Origin));

        var rows = await messages
            .Where(m => m.Direction == MessageDirection.Inbound)
            .InRange(m => m.CreatedAt, from, to)
            .Select(m => new
            {
                m.ConversationId,
                m.CreatedAt,
                LastReply = replies.Where(r => r.ConversationId == m.ConversationId && r.CreatedAt < m.CreatedAt)
                    .Max(r => (DateTimeOffset?)r.CreatedAt),
            })
            // Only the first customer message since the last reply starts a wait; later ones join it.
            .Where(x => !messages.Any(p => p.ConversationId == x.ConversationId && p.Direction == MessageDirection.Inbound
                && p.CreatedAt < x.CreatedAt && (x.LastReply == null || p.CreatedAt > x.LastReply)))
            .Select(x => new
            {
                x.CreatedAt,
                Reply = replies.Where(r => r.ConversationId == x.ConversationId && r.CreatedAt >= x.CreatedAt)
                    .OrderBy(r => r.CreatedAt)
                    .Select(r => new { r.CreatedAt, r.Origin })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new ReportWait(r.CreatedAt, r.Reply?.CreatedAt, r.Reply?.Origin == MessageOrigin.Ai))
            .ToList();
    }

    public async Task<ReportReplyCounts> GetReplyCountsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var replies = _db.Messages.AsNoTracking()
            .Where(m => m.ClientId == clientId && m.Direction == MessageDirection.Outbound && ReplyOrigins.Contains(m.Origin))
            .InRange(m => m.CreatedAt, from, to);
        var ai = replies.Where(m => m.Origin == MessageOrigin.Ai);

        var aiReplies = await ai.CountAsync(ct);
        var teamReplies = await replies.CountAsync(m => m.Origin != MessageOrigin.Ai, ct);
        var aiConversations = await ai.Select(m => m.ConversationId).Distinct().CountAsync(ct);
        var aiOnlyConversations = await ai.Select(m => m.ConversationId).Distinct()
            .CountAsync(id => !replies.Any(m => m.ConversationId == id && m.Origin != MessageOrigin.Ai), ct);
        return new ReportReplyCounts(aiReplies, teamReplies, aiConversations, aiOnlyConversations);
    }

    public async Task<IReadOnlyList<ReportMessageBucket>> GetMessageBucketsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var rows = await _db.Messages.AsNoTracking()
            .Where(m => m.ClientId == clientId)
            .InRange(m => m.CreatedAt, from, to)
            .GroupBy(m => new
            {
                m.CreatedAt.Year, m.CreatedAt.Month, m.CreatedAt.Day, m.CreatedAt.Hour, Quarter = m.CreatedAt.Minute / 15,
            })
            .Select(g => new
            {
                g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, g.Key.Quarter,
                Received = g.Count(m => m.Direction == MessageDirection.Inbound),
                Sent = g.Count(m => m.Direction == MessageDirection.Outbound),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new ReportMessageBucket(new DateTimeOffset(r.Year, r.Month, r.Day, r.Hour, r.Quarter * 15, 0, TimeSpan.Zero),
                r.Received, r.Sent))
            .OrderBy(b => b.SlotStart)
            .ToList();
    }

    public async Task<IReadOnlyList<ReportUsageRow>> GetSettledUsageAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.ClientId == clientId && u.ChargeStatus == ChargeStatus.Settled)
            .InRange(u => u.OccurredAt, from, to)
            .GroupBy(u => new { u.EventType, u.Currency })
            .Select(g => new { g.Key.EventType, g.Key.Currency, Count = g.Count(), Quantity = g.Sum(u => u.Quantity), Amount = g.Sum(u => (u.Amount ?? 0) - u.RefundedAmount) })
            .OrderByDescending(r => r.Amount).ThenBy(r => r.EventType)
            .Select(r => new ReportUsageRow(r.EventType, r.Currency, r.Count, r.Quantity, r.Amount))
            .ToListAsync(ct);
}
