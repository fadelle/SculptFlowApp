using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.PlatformAdmin;

namespace Omni.App.Business.Mappers.PlatformAdmin;

/// <summary>Turns what a trace touched (steps, messages, charges, events, saved logs) into one timeline, oldest first.</summary>
public static class MessageTraceMapper
{
    private static readonly Dictionary<string, string?> NoDetail = new();

    public static List<MessageTraceEntry> Timeline(
        IReadOnlyList<TraceStepRow> steps,
        IReadOnlyList<TraceMessageRow> messages,
        IReadOnlyList<TraceBillingRow> billing,
        IReadOnlyList<TraceEventRow> events,
        IReadOnlyList<AppLogRow> logs)
    {
        var entries = new List<MessageTraceEntry>();

        foreach (var s in steps)
        {
            var detail = AppLogMapper.Properties(s.Detail);
            entries.Add(new MessageTraceEntry(s.OccurredAt, "step", s.Step, s.Outcome, Describe(s.Step, s.Channel, detail), s.DurationMs,
                s.ClientId, s.ConversationId, s.MessageId, s.N8nExecutionId, null, detail));
        }

        foreach (var m in messages)
        {
            var ids = (m.ClientId, m.ConversationId, m.Id);
            entries.Add(Entry(m.CreatedAt, "message", "message." + m.Direction, TraceOutcomes.Ok,
                $"{Who(m)} {m.MessageType} message on {m.Channel}" + (m.ExternalMessageId is null ? "" : " (provider id saved)"), ids));
            // Delivery statuses come from the message row (outbound only: an inbound message has no delivery).
            if (m.Direction != MessageDirection.Outbound) continue;
            if (m.DeliveredAt is { } delivered) entries.Add(Entry(delivered, "status", "status.delivered", TraceOutcomes.Ok, "Delivered to the contact's phone", ids));
            if (m.ReadAt is { } read) entries.Add(Entry(read, "status", "status.read", TraceOutcomes.Ok, "Read by the contact", ids));
            if (m.FailedAt is { } failed)
            {
                entries.Add(Entry(failed, "status", "status.failed", TraceOutcomes.Failed,
                    "Delivery failed" + (m.FailureCode is null ? "" : $" ({m.FailureCode})") + (m.FailureReason is null ? "" : ": " + m.FailureReason), ids));
            }
        }

        foreach (var b in billing)
        {
            var ids = ((Guid?)null, (Guid?)null, b.MessageId);
            if (b.ReservedAt is { } reserved)
                entries.Add(Entry(reserved, "billing", "billing.reserved", TraceOutcomes.Ok, $"Charge reserved: {Money(b.Amount, b.Currency)}", ids));
            if (b.SettledAt is { } settled)
                entries.Add(Entry(settled, "billing", "billing.charged", TraceOutcomes.Ok, $"Charged {Money(b.Amount, b.Currency)}", ids));
            if (b.ReleasedAt is { } released)
                entries.Add(Entry(released, "billing", "billing.released", TraceOutcomes.Ok, "Charge released" + (b.ReleaseReason is null ? "" : ": " + b.ReleaseReason), ids));
            if (b.ChargeStatus == ChargeStatus.Failed)
                entries.Add(Entry(b.OccurredAt, "billing", "billing.failed", TraceOutcomes.Failed, "Charge failed" + (b.FailureReason is null ? "" : ": " + b.FailureReason), ids));
            if (b.ChargeStatus == ChargeStatus.NotCharged && b.ReservedAt is null)
                entries.Add(Entry(b.OccurredAt, "billing", "billing.not_charged", TraceOutcomes.Ok, "Not charged", ids));
        }

        foreach (var e in events)
        {
            entries.Add(new MessageTraceEntry(e.CreatedAt, "event", e.EventType, TraceOutcomes.Ok,
                e.EventType.Replace('_', ' ') + (e.Source is null ? "" : $" ({e.Source})"), null, null, e.ConversationId, null, null, null,
                AppLogMapper.Properties(e.Metadata)));
        }

        foreach (var l in logs)
        {
            var isWarning = l.Level == AppLogLevels.Warning;
            entries.Add(new MessageTraceEntry(l.OccurredAt, isWarning ? "warning" : "error", l.Category, isWarning ? TraceOutcomes.Ok : TraceOutcomes.Failed,
                l.Message, null, l.ClientId, l.ConversationId, l.MessageId, null, l.Id, NoDetail));
        }

        return entries.OrderBy(e => e.At).ThenBy(e => KindOrder(e.Kind)).ToList();
    }

    /// <summary>A plain sentence for a step, from its name and facts.</summary>
    public static string Describe(string step, string? channel, IReadOnlyDictionary<string, string?> detail)
    {
        string? D(string key) => detail.TryGetValue(key, out var v) ? v : null;
        var on = channel is null ? "" : $" ({channel})";
        return step switch
        {
            TraceSteps.WebhookReceived => $"Webhook received{on}" + (D("kinds") is { } kinds ? $": {kinds}" : D("type") is { } type ? $": {type}" : ""),
            TraceSteps.WebhookRejected => $"Webhook rejected{on}: {D("reason")}",
            TraceSteps.WebhookClientUnresolved => $"No client owns this number{on}; event {D("kind")} ignored",
            TraceSteps.WebhookIgnored => $"Webhook ignored{on}: {D("reason")}",
            TraceSteps.MessageSaved => $"Saved to the inbox: {D("sender")} {D("type")} ({D("direction")})",
            TraceSteps.MessageDeduplicated => "Duplicate delivery; the message was already saved",
            TraceSteps.ConversationReopened => "Closed conversation reopened by the contact",
            TraceSteps.ConversationModeChanged => $"Conversation moved to the team: {D("reason")}",
            TraceSteps.InboxNotified => "Inbox updated for members (live)",
            TraceSteps.AutoReplySkipped => $"No auto-reply to the {D("trigger")} conversation: {D("reason")}",
            TraceSteps.BotSkipped => $"Bot not asked: {D("reason")}",
            TraceSteps.BotTriggered => $"Bot (n8n) asked to reply (answered {D("status")})",
            TraceSteps.BotTriggerFailed => "Bot (n8n) call failed: " + (D("error") ?? $"answered {D("status")}"),
            TraceSteps.BotToolCalled => $"Bot used tool {D("tool")} (answered {D("status")})",
            TraceSteps.ContactLanguageIgnored => $"Contact language from the bot not understood ({D("value")}); kept {D("kept") ?? "none"}",
            TraceSteps.SendRequested => $"Send requested: {D("kind")}" + (D("template") is { } template ? $" template {template}" : ""),
            TraceSteps.SendBlocked => $"Send blocked before it went out: {D("error")}",
            TraceSteps.SendRejectedMode => $"AI reply not sent: conversation is with the team ({D("mode")})",
            TraceSteps.SendProviderAccepted => $"Accepted by {channel ?? "the provider"}",
            TraceSteps.SendProviderFailed => $"{channel ?? "Provider"} refused the send: {D("error")}",
            _ => step
        };
    }

    private static MessageTraceEntry Entry(DateTimeOffset at, string kind, string name, string outcome, string summary,
        (Guid? ClientId, Guid? ConversationId, Guid? MessageId) ids) =>
        new(at, kind, name, outcome, summary, null, ids.ClientId, ids.ConversationId, ids.MessageId, null, null, NoDetail);

    private static string Who(TraceMessageRow m) => m.SenderType switch
    {
        MessageSenderType.Contact => "Contact",
        MessageSenderType.Ai => "AI",
        MessageSenderType.Member => "Member",
        _ => "System"
    };

    private static string Money(decimal? amount, string currency) => amount is { } a ? $"{a:0.####} {currency}" : "(no rate)";

    // At the same moment: the step, then the message row, then everything that follows from it.
    private static int KindOrder(string kind) => kind switch
    {
        "step" => 0,
        "message" => 1,
        "billing" => 2,
        "status" => 3,
        "event" => 4,
        _ => 5
    };
}
