using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Persistence.Repositories.PlatformAdmin;

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
            .Select(s => new TraceStepRow(s.Id, s.TraceId, s.OccurredAt, s.Step, s.Outcome, s.DurationMs, s.ClientId, s.ConversationId,
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
            .Select(m => new TraceMessageRow(m.Id, m.ClientId, m.ConversationId, m.Direction, m.SenderType, m.Origin, m.Channel, m.MessageType,
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
                l.TraceId, l.ClientId, l.ConversationId, l.MessageId, l.Method, l.Route))
            .ToListAsync(ct);

    public async Task<(List<TraceSummaryRow> Rows, int Total)> ListReadOnlyAsync(MessageTraceFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var steps = _db.MessageTraceSteps.AsNoTracking().Where(s => s.OccurredAt >= filter.From);
        if (filter.ClientId is { } clientId)
        {
            var ofClient = steps.Where(s => s.ClientId == clientId).Select(s => s.TraceId);
            steps = steps.Where(s => ofClient.Contains(s.TraceId));
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
        var outcomes = await OutcomesReadOnlyAsync(traceIds, DateTimeOffset.UtcNow, ct);
        // The conversation comes from the first step that names one (QA t7-10: the first step naming a client often doesn't).
        var conversations = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId) && s.ConversationId != null)
            .GroupBy(s => s.TraceId)
            .Select(g => new { TraceId = g.Key, Id = g.Min(s => s.Id) })
            .Join(_db.MessageTraceSteps, x => x.Id, s => s.Id, (x, s) => new { x.TraceId, s.ConversationId })
            .ToDictionaryAsync(x => x.TraceId, x => x.ConversationId, ct);

        // The trace's client/conversation/channel come from its first step that names them (webhook.received names none).
        var namedIds = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId) && s.ClientId != null)
            .GroupBy(s => s.TraceId)
            .Select(g => g.Min(s => s.Id))
            .ToListAsync(ct);
        var stepIds = page.Select(g => g.FirstId).Concat(page.Where(g => g.FailedId != null).Select(g => g.FailedId!.Value)).Concat(namedIds).Distinct().ToList();
        var picked = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => stepIds.Contains(s.Id))
            .Select(s => new { s.Id, s.TraceId, s.Step, s.ClientId, s.ConversationId, s.Channel, s.Detail })
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
            var (outcome, reason) = outcomes.GetValueOrDefault(g.TraceId, (TraceResults.Other, null));
            return new TraceSummaryRow(g.TraceId, g.StartedAt, g.EndedAt, g.Steps, g.FailedSteps, errors.GetValueOrDefault(g.TraceId),
                about?.ClientId ?? first.ClientId, conversations.GetValueOrDefault(g.TraceId) ?? about?.ConversationId ?? first.ConversationId,
                about?.Channel ?? first.Channel, first.Step, failed?.Step, failed?.Detail, outcome, reason);
        }).ToList();
        return (rows, total);
    }

    // Steps that mean "not answered on purpose"; their detail's "reason" is the outcome's reason.
    private static readonly string[] SkipSteps =
    [
        TraceSteps.BotSkipped, TraceSteps.BotReplyStopped, TraceSteps.SendBlocked, TraceSteps.SendRejectedMode,
        TraceSteps.WebhookClientUnresolved, TraceSteps.WebhookIgnored
    ];

    public async Task<Dictionary<string, (string Outcome, string? Reason)>> OutcomesReadOnlyAsync(IReadOnlyCollection<string> traceIds,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (traceIds.Count == 0) return [];
        var skipSteps = SkipSteps;
        var facts = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId))
            .GroupBy(s => s.TraceId)
            .Select(g => new
            {
                TraceId = g.Key,
                EndedAt = g.Max(s => s.OccurredAt),
                Accepted = g.Count(s => s.Step == TraceSteps.SendProviderAccepted),
                ModeChanged = g.Count(s => s.Step == TraceSteps.ConversationModeChanged),
                Triggered = g.Count(s => s.Step == TraceSteps.BotTriggered || s.Step == TraceSteps.BotRunStarted),
                Saved = g.Count(s => s.Step == TraceSteps.MessageSaved),
                FailedId = g.Min(s => s.Outcome == TraceOutcomes.Failed ? (long?)s.Id : null),
                SkipId = g.Min(s => skipSteps.Contains(s.Step) ? (long?)s.Id : null)
            })
            .ToListAsync(ct);
        var pickIds = facts.Select(f => f.FailedId).Concat(facts.Select(f => f.SkipId)).Where(id => id != null).Select(id => id!.Value).ToList();
        var picked = await _db.MessageTraceSteps.AsNoTracking().Where(s => pickIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Step, s.Detail }).ToDictionaryAsync(s => s.Id, ct);
        // A handoff the bot made (its handoff action, or the handover after a failed run) that worked.
        var actions = await _db.MessageTraceSteps.AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId) && s.Step == TraceSteps.BotActionResult && s.Outcome == TraceOutcomes.Ok)
            .Select(s => new { s.TraceId, s.Detail }).ToListAsync(ct);
        var handedOff = actions.Where(a => IsHandoffAction(a.Detail)).Select(a => a.TraceId).ToHashSet();

        return facts.ToDictionary(f => f.TraceId, f => TraceResultRule.Of(new TraceFacts(
            f.Accepted, f.ModeChanged, handedOff.Contains(f.TraceId), f.Triggered, f.Saved,
            f.FailedId is { } fid ? picked[fid].Step : null,
            f.SkipId is { } sid ? picked[sid].Step : null,
            f.SkipId is { } sid2 ? picked[sid2].Detail : null,
            f.EndedAt), now));
    }

    private static bool IsHandoffAction(string detail)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(detail);
            return doc.RootElement.TryGetProperty("action", out var action)
                && action.GetString() is BotDecision.Handoff or "run_failed";
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public async Task<(List<UnansweredRow> Rows, int Total)> ListUnansweredReadOnlyAsync(DateTimeOffset from, DateTimeOffset before, Guid? clientId,
        int skip, int take, CancellationToken ct = default)
    {
        var conversations = _db.Conversations.AsNoTracking()
            .Where(c => c.LastMessageDirection == MessageDirection.Inbound && c.LastMessageAt >= from && c.LastMessageAt <= before
                        && c.Status != ConversationStatus.Closed);
        if (clientId is { } id) conversations = conversations.Where(c => c.ClientId == id);
        var total = await conversations.CountAsync(ct);
        if (total == 0) return ([], 0);

        var page = await conversations.OrderBy(c => c.LastMessageAt).ThenBy(c => c.Id).Skip(skip).Take(take)
            .Select(c => new
            {
                c.Id, c.ClientId, ClientName = _db.Clients.Where(x => x.Id == c.ClientId).Select(x => x.Name).FirstOrDefault() ?? "", ContactName = c.Contact!.FullName,
                c.Channel, c.Mode, c.LastMessageAt,
                Last = _db.Messages.Where(m => m.ConversationId == c.Id && m.Direction == MessageDirection.Inbound)
                    .OrderByDescending(m => m.CreatedAt).Select(m => new { m.Id, m.MessageType, m.TraceId }).FirstOrDefault()
            })
            .ToListAsync(ct);
        var traceIds = page.Where(p => p.Last?.TraceId != null).Select(p => p.Last!.TraceId!).Distinct().ToList();
        var outcomes = await OutcomesReadOnlyAsync(traceIds, DateTimeOffset.UtcNow, ct);
        var rows = page.Select(p =>
        {
            (string Outcome, string? Reason)? outcome = p.Last?.TraceId is { } t && outcomes.TryGetValue(t, out var o) ? o : null;
            return new UnansweredRow(p.Id, p.ClientId, p.ClientName, p.ContactName, p.Channel, p.Mode, p.LastMessageAt!.Value,
                p.Last?.Id, p.Last?.MessageType, p.Last?.TraceId, outcome?.Outcome, outcome?.Reason);
        }).ToList();
        return (rows, total);
    }

    public async Task<List<AiUsageCounts>> AiUsageReadOnlyAsync(DateTimeOffset from, Guid? clientId, CancellationToken ct = default) =>
        await _db.Database.SqlQuery<AiUsageCounts>($"""
            select s.client_id as "ClientId", c.name as "ClientName",
                   count(*) filter (where s.step = 'bot.run_started') as "Runs",
                   count(*) filter (where s.step = 'bot.run_failed') as "FailedRuns",
                   count(*) filter (where s.step = 'bot.model_called' and s.outcome = 'ok') as "ModelCalls",
                   coalesce(sum(case when s.step = 'bot.model_called' and jsonb_typeof(s.detail -> 'inputTokens') = 'number'
                                     then (s.detail ->> 'inputTokens')::bigint end), 0)::bigint as "InputTokens",
                   coalesce(sum(case when s.step = 'bot.model_called' and jsonb_typeof(s.detail -> 'outputTokens') = 'number'
                                     then (s.detail ->> 'outputTokens')::bigint end), 0)::bigint as "OutputTokens"
            from activity.message_trace_steps s
            join core.clients c on c.id = s.client_id
            where s.occurred_at >= {from}
              and s.step in ('bot.run_started', 'bot.run_failed', 'bot.model_called')
              and ({clientId}::uuid is null or s.client_id = {clientId}::uuid)
            group by s.client_id, c.name
            """).ToListAsync(ct);

    public async Task<List<AiReplyRevenue>> AiReplyRevenueReadOnlyAsync(DateTimeOffset from, Guid? clientId, CancellationToken ct = default) =>
        await _db.BillingUsageRecords.AsNoTracking()
            .Where(u => u.EventType == BillableEventTypes.AiReply && u.ChargeStatus == ChargeStatus.Settled && u.OccurredAt >= from
                        && (clientId == null || u.ClientId == clientId))
            .GroupBy(u => new { u.ClientId, u.Currency })
            .Select(g => new AiReplyRevenue(g.Key.ClientId, g.Sum(u => (u.Amount ?? 0) - u.RefundedAmount), g.Key.Currency))
            .ToListAsync(ct);

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