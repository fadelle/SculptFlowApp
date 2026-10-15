using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Persistence.Repositories.PlatformAdmin;

public class MessageTraceAdminRepository : IMessageTraceAdminRepository
{
    private static readonly string[] ErrorLevels = [AppLogLevels.Error, AppLogLevels.Critical];

    private readonly ApplicationDbContext _db;

    public MessageTraceAdminRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<List<TraceStepRow>> StepsOfTraceReadOnlyAsync(string traceId, CancellationToken ct = default) =>
        _db.MessageTraceSteps.AsNoTracking()
            .Where(s => s.TraceId == traceId)
            .OrderBy(s => s.OccurredAt).ThenBy(s => s.Id)
            .Select(s => new TraceStepRow(s.Id, s.TraceId, s.OccurredAt, s.Step, s.Outcome, s.DurationMs, s.ClinicId, s.ConversationId,
                s.MessageId, s.ExternalMessageId, s.Channel, s.N8nExecutionId, s.Detail))
            .ToListAsync(ct);

    public async Task<string?> TraceIdOfMessageReadOnlyAsync(Guid messageId, CancellationToken ct = default)
    {
        var own = await _db.Messages.AsNoTracking().Where(m => m.Id == messageId).Select(m => m.TraceId).FirstOrDefaultAsync(ct);
        if (own is not null) return own;
        return await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => s.MessageId == messageId)
            .OrderBy(s => s.OccurredAt)
            .Select(s => s.TraceId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> TraceIdOfExternalMessageReadOnlyAsync(string externalMessageId, CancellationToken ct = default)
    {
        var own = await _db.Messages.AsNoTracking()
            .Where(m => m.ExternalMessageId == externalMessageId && m.TraceId != null)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.TraceId)
            .FirstOrDefaultAsync(ct);
        if (own is not null) return own;
        return await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => s.ExternalMessageId == externalMessageId)
            .OrderBy(s => s.OccurredAt)
            .Select(s => s.TraceId)
            .FirstOrDefaultAsync(ct);
    }

    public Task<List<TraceMessageRow>> MessagesReadOnlyAsync(string traceId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        var ids = messageIds.ToList();
        return _db.Messages.AsNoTracking()
            .Where(m => m.TraceId == traceId || ids.Contains(m.Id))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new TraceMessageRow(m.Id, m.ClinicId, m.ConversationId, m.Direction, m.SenderType, m.Origin, m.Channel, m.MessageType,
                m.ExternalMessageId, m.DeliveryStatus, m.CreatedAt, m.SentAt, m.DeliveredAt, m.ReadAt, m.FailedAt, m.FailureCode,
                m.FailureReason, m.TraceId))
            .ToListAsync(ct);
    }

    public Task<List<TraceBillingRow>> BillingReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        var ids = messageIds.ToList();
        return _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.MessageId != null && ids.Contains(u.MessageId.Value))
            .OrderBy(u => u.OccurredAt)
            .Select(u => new TraceBillingRow(u.MessageId!.Value, u.ChargeStatus, u.Amount, u.Currency, u.OccurredAt, u.ReservedAt, u.SettledAt,
                u.ReleasedAt, u.ReleaseReason, u.FailureReason))
            .ToListAsync(ct);
    }

    public Task<List<TraceEventRow>> EventsReadOnlyAsync(IReadOnlyCollection<Guid> conversationIds, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var ids = conversationIds.ToList();
        return _db.Events.AsNoTracking()
            .Where(e => e.ConversationId != null && ids.Contains(e.ConversationId.Value) && e.CreatedAt >= from && e.CreatedAt <= to)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new TraceEventRow(e.CreatedAt, e.EventType, e.Source, e.ConversationId, e.Metadata))
            .ToListAsync(ct);
    }

    public Task<List<AppLogRow>> LogsOfTraceReadOnlyAsync(string traceId, CancellationToken ct = default) =>
        _db.AppLogs.AsNoTracking()
            .Where(l => l.TraceId == traceId)
            .OrderBy(l => l.OccurredAt).ThenBy(l => l.Id)
            .Select(l => new AppLogRow(l.Id, l.OccurredAt, l.Level, l.Category, l.Message, l.ExceptionType, l.Fingerprint, l.RepeatCount,
                l.TraceId, l.ClinicId, l.ConversationId, l.MessageId, l.Method, l.Route))
            .ToListAsync(ct);

    public async Task<(List<TraceSummaryRow> Rows, int Total)> ListReadOnlyAsync(MessageTraceFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var steps = _db.MessageTraceSteps.AsNoTracking().Where(s => s.OccurredAt >= filter.From);
        if (filter.ClinicId is { } clinicId)
        {
            var ofClinic = steps.Where(s => s.ClinicId == clinicId).Select(s => s.TraceId);
            steps = steps.Where(s => ofClinic.Contains(s.TraceId));
        }
        if (filter.ConversationId is { } conversationId)
        {
            var ofConversation = steps.Where(s => s.ConversationId == conversationId).Select(s => s.TraceId);
            steps = steps.Where(s => ofConversation.Contains(s.TraceId));
        }
        if (filter.FailedOnly)
        {
            var failedSteps = steps.Where(s => s.Outcome == TraceOutcomes.Failed).Select(s => s.TraceId);
            var withErrors = _db.AppLogs.Where(l => l.TraceId != null && l.OccurredAt >= filter.From && ErrorLevels.Contains(l.Level))
                .Select(l => l.TraceId!);
            steps = steps.Where(s => failedSteps.Contains(s.TraceId) || withErrors.Contains(s.TraceId));
        }

        var groups = steps.GroupBy(s => s.TraceId).Select(g => new
        {
            TraceId = g.Key,
            StartedAt = g.Min(s => s.OccurredAt),
            EndedAt = g.Max(s => s.OccurredAt),
            Steps = g.Count(),
            FailedSteps = g.Count(s => s.Outcome == TraceOutcomes.Failed),
            FirstId = g.Min(s => s.Id),
            FailedId = g.Min(s => s.Outcome == TraceOutcomes.Failed ? (long?)s.Id : null)
        });
        var total = await groups.CountAsync(ct);
        if (total == 0) return ([], 0);

        var page = await groups.OrderByDescending(g => g.EndedAt).ThenBy(g => g.TraceId).Skip(skip).Take(take).ToListAsync(ct);
        var traceIds = page.Select(g => g.TraceId).ToList();

        // The trace's clinic/conversation/channel come from its first step that names them (webhook.received names none).
        var namedIds = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId) && s.ClinicId != null)
            .GroupBy(s => s.TraceId)
            .Select(g => g.Min(s => s.Id))
            .ToListAsync(ct);
        var stepIds = page.Select(g => g.FirstId).Concat(page.Where(g => g.FailedId != null).Select(g => g.FailedId!.Value)).Concat(namedIds).Distinct().ToList();
        var picked = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => stepIds.Contains(s.Id))
            .Select(s => new { s.Id, s.TraceId, s.Step, s.ClinicId, s.ConversationId, s.Channel, s.Detail })
            .ToDictionaryAsync(s => s.Id, ct);
        var named = picked.Values.Where(s => namedIds.Contains(s.Id)).ToDictionary(s => s.TraceId);
        var errors = await _db.AppLogs.AsNoTracking()
            .Where(l => l.TraceId != null && traceIds.Contains(l.TraceId) && ErrorLevels.Contains(l.Level))
            .GroupBy(l => l.TraceId!)
            .Select(g => new { TraceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TraceId, g => g.Count, ct);

        var rows = page.Select(g =>
        {
            var first = picked[g.FirstId];
            named.TryGetValue(g.TraceId, out var about);
            var failed = g.FailedId is { } failedId ? picked[failedId] : null;
            return new TraceSummaryRow(g.TraceId, g.StartedAt, g.EndedAt, g.Steps, g.FailedSteps, errors.GetValueOrDefault(g.TraceId),
                about?.ClinicId ?? first.ClinicId, about?.ConversationId ?? first.ConversationId, about?.Channel ?? first.Channel,
                first.Step, failed?.Step, failed?.Detail);
        }).ToList();
        return (rows, total);
    }

    public async Task<TraceCounts> CountSinceReadOnlyAsync(DateTimeOffset from, CancellationToken ct = default)
    {
        var steps = _db.MessageTraceSteps.AsNoTracking().Where(s => s.OccurredAt >= from);
        var traces = await steps.Select(s => s.TraceId).Distinct().LongCountAsync(ct);
        var failedSteps = steps.Where(s => s.Outcome == TraceOutcomes.Failed);
        var withErrors = _db.AppLogs.Where(l => l.TraceId != null && l.OccurredAt >= from && ErrorLevels.Contains(l.Level)).Select(l => l.TraceId!);
        var failed = await steps.Where(s => s.Outcome == TraceOutcomes.Failed || withErrors.Contains(s.TraceId))
            .Select(s => s.TraceId).Distinct().LongCountAsync(ct);
        var lastFailed = await failedSteps.MaxAsync(s => (DateTimeOffset?)s.OccurredAt, ct);
        return new TraceCounts(traces, failed, lastFailed);
    }
}