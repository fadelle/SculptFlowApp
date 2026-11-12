using System.Text.RegularExpressions;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public partial class MessageTraceAdminService : IMessageTraceAdminService
{
    private readonly IMessageTraceAdminRepository _traces;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public MessageTraceAdminService(IMessageTraceAdminRepository traces, IConfigManager config, TimeProvider time)
    {
        _traces = traces;
        _config = config;
        _time = time;
    }

    public async Task<MessageTrace> GetAsync(string traceId, CancellationToken ct)
    {
        var id = Normalize(traceId) ?? throw new KeyNotFoundException($"'{traceId}' is not a trace id.");
        var steps = await _traces.StepsOfTraceReadOnlyAsync(id, ct);
        var messages = await _traces.MessagesReadOnlyAsync(id, steps.Where(s => s.MessageId != null).Select(s => s.MessageId!.Value).Distinct().ToList(), ct);
        var logs = await _traces.LogsOfTraceReadOnlyAsync(id, ct);
        if (steps.Count == 0 && messages.Count == 0 && logs.Count == 0)
        {
            throw new KeyNotFoundException($"Nothing is recorded for trace {id} (it may be past retention).");
        }

        var messageIds = messages.Select(m => m.Id).ToList();
        var conversationIds = steps.Where(s => s.ConversationId != null).Select(s => s.ConversationId!.Value)
            .Concat(messages.Select(m => m.ConversationId)).Distinct().ToList();
        var times = steps.Select(s => s.OccurredAt).Concat(messages.Select(m => m.CreatedAt)).Concat(logs.Select(l => l.OccurredAt)).ToList();
        var started = times.Min();
        var ended = times.Max();
        var billing = messageIds.Count == 0 ? [] : await _traces.BillingReadOnlyAsync(messageIds, ct);
        var events = conversationIds.Count == 0 ? [] : await _traces.EventsReadOnlyAsync(conversationIds,
            started.AddMinutes(-1), ended.AddMinutes(MessageTraceLimits.EventWindowMinutes), ct);

        var entries = MessageTraceMapper.Timeline(steps, messages, billing, events, logs);
        var clientId = steps.Select(s => s.ClientId).Concat(messages.Select(m => (Guid?)m.ClientId)).FirstOrDefault(c => c != null);
        var hasFailure = entries.Any(e => e.Outcome == TraceOutcomes.Failed);
        var outcomes = steps.Count == 0 ? null : await _traces.OutcomesReadOnlyAsync([id], _time.GetUtcNow(), ct);
        var (outcome, reason) = outcomes is not null && outcomes.TryGetValue(id, out var o) ? o : (TraceResults.Other, null);
        return new MessageTrace(id, started, entries.Count == 0 ? ended : entries.Max(e => e.At), clientId, conversationIds, messageIds,
            hasFailure, entries, outcome, reason);
    }

    public async Task<MessageTrace> ForMessageAsync(Guid messageId, CancellationToken ct)
    {
        var traceId = await _traces.TraceIdOfMessageReadOnlyAsync(messageId, ct)
                      ?? throw new KeyNotFoundException($"Message {messageId} has no trace (it was saved before tracing, or its trace was purged).");
        return await GetAsync(traceId, ct);
    }

    public async Task<TraceReference> ResolveAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter a trace id, message id or provider message id.");
        var q = query.Trim();
        if (Normalize(q) is { } traceId) return new TraceReference(traceId);
        var found = Guid.TryParse(q, out var messageId)
            ? await _traces.TraceIdOfMessageReadOnlyAsync(messageId, ct)
            : await _traces.TraceIdOfExternalMessageReadOnlyAsync(q, ct);
        return found is null
            ? throw new KeyNotFoundException($"No trace found for '{q}'.")
            : new TraceReference(found);
    }

    public async Task<MessageTracePage> ListAsync(int hours, Guid? clientId, Guid? conversationId, bool failedOnly, int page, int pageSize, CancellationToken ct)
    {
        hours = Math.Clamp(hours, 1, MessageTraceLimits.MaxReportHours);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MessageTraceLimits.MaxPageSize);
        var to = _time.GetUtcNow();
        var from = to.AddHours(-hours);
        var (rows, total) = await _traces.ListReadOnlyAsync(new MessageTraceFilter(from, clientId, conversationId, failedOnly),
            (page - 1) * pageSize, pageSize, ct);
        return new MessageTracePage(from, to, page, pageSize, total, _config.MessageTraceEnabled, _config.MessageTraceRetentionDays,
            _config.MessageTraceFailedRetentionDays, rows);
    }

    public async Task<MessageTraceSummary> SummaryAsync(int hours, CancellationToken ct)
    {
        hours = Math.Clamp(hours, 1, MessageTraceLimits.MaxReportHours);
        var from = _time.GetUtcNow().AddHours(-hours);
        var counts = await _traces.CountSinceReadOnlyAsync(from, ct);
        return new MessageTraceSummary(hours, from, _config.MessageTraceEnabled, counts.Traces, counts.FailedTraces, counts.LastFailedAt);
    }

    public async Task<UnansweredPage> UnansweredAsync(int hours, int minutes, Guid? clientId, int page, int pageSize, CancellationToken ct)
    {
        hours = Math.Clamp(hours, 1, MessageTraceLimits.MaxReportHours);
        minutes = Math.Clamp(minutes, 1, 24 * 60);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MessageTraceLimits.MaxPageSize);
        var now = _time.GetUtcNow();
        var from = now.AddHours(-hours);
        var before = now.AddMinutes(-minutes);
        var (rows, total) = await _traces.ListUnansweredReadOnlyAsync(from, before, clientId, (page - 1) * pageSize, pageSize, ct);
        return new UnansweredPage(from, before, minutes, page, pageSize, total, rows);
    }

    public async Task<AiUsageReport> AiUsageAsync(int days, Guid? clientId, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 90);
        var to = _time.GetUtcNow();
        var from = to.AddDays(-days);
        var inputPrice = _config.AiInputTokenPriceUsdPerMillion;
        var outputPrice = _config.AiOutputTokenPriceUsdPerMillion;
        var usage = await _traces.AiUsageReadOnlyAsync(from, clientId, ct);
        var revenue = (await _traces.AiReplyRevenueReadOnlyAsync(from, clientId, ct)).GroupBy(r => r.ClientId).ToDictionary(g => g.Key, g => g.First());
        var rows = usage.Select(u =>
            {
                revenue.TryGetValue(u.ClientId, out var paid);
                var cost = Math.Round((u.InputTokens * inputPrice + u.OutputTokens * outputPrice) / 1_000_000m, 4);
                return new AiUsageRow(u.ClientId, u.ClientName, u.Runs, u.FailedRuns, u.ModelCalls, u.InputTokens, u.OutputTokens, cost,
                    paid?.Amount ?? 0, paid?.Currency);
            })
            .OrderByDescending(r => r.ModelCalls).ThenBy(r => r.ClientName)
            .ToList();
        return new AiUsageReport(from, to, inputPrice, outputPrice, _config.MessageTraceRetentionDays, rows);
    }

    /// <summary>A trace id in its stored form (32 lower-case hex characters), or null when it isn't one.</summary>
    private static string? Normalize(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is not null && TraceIdPattern().IsMatch(v) ? v : null;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceIdPattern();
}