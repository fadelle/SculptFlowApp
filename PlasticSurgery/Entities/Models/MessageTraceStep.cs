namespace PlasticSurgery.Entities.Models;

/// <summary>
/// One step a message took through the app (activity.message_trace_steps), written by MessageTraceFlushJob from what
/// IMessageTracer queued. Steps sharing <see cref="TraceId"/> tell one message's story; delivery statuses, billing and
/// business events are read from their own tables by the trace view, not copied here.
/// </summary>
public class MessageTraceStep
{
    public long Id { get; set; }

    /// <summary>W3C trace id (32 hex characters) of the request or job the step happened in.</summary>
    public string TraceId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>What happened, from the TraceSteps vocabulary ("message.saved", "bot.triggered", ...).</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>"ok", "skipped" or "failed" (TraceOutcomes).</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>How long the step took, for steps that wait on something (provider call, n8n, a bot tool).</summary>
    public double? DurationMs { get; set; }

    public Guid? ClinicId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string? ExternalMessageId { get; set; }
    public string? Channel { get; set; }

    /// <summary>n8n's execution id, when n8n sent X-N8n-Execution-Id on its callback.</summary>
    public string? N8nExecutionId { get; set; }

    /// <summary>Small JSON object with the step's facts (reasons, codes, counts). Never message text, phones or tokens.</summary>
    public string Detail { get; set; } = "{}";

    public string? Instance { get; set; }
}