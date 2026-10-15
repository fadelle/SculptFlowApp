namespace PlasticSurgery.Entities.Models;

/// <summary>
/// One warning, error or critical the app logged (activity.app_logs). Captured by DatabaseLoggerProvider, queued in
/// AppLogCollector and written by AppLogFlushJob. <see cref="Fingerprint"/> groups the same problem across its values;
/// <see cref="RepeatCount"/> is how many occurrences the row stands for (more than 1 only for the summary row written
/// when ErrorLog:MaxPerFingerprintPerMinute was reached, or for the "entries dropped" row).
/// </summary>
public class AppLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>"Warning", "Error" or "Critical" (AppLogLevels).</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>The logger name, e.g. "PlasticSurgery.Business.HttpClients.N8n.AiTriggerNotifier".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The rendered message (capped at AppLogLimits.MaxMessageLength).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The message template before values were filled in ("n8n AI webhook returned {StatusCode} ...").</summary>
    public string? MessageTemplate { get; set; }

    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }

    /// <summary>The exception's full text (type, message, stack, inner exceptions), capped at AppLogLimits.MaxStackLength.</summary>
    public string? StackTrace { get; set; }

    public string Fingerprint { get; set; } = string.Empty;
    public int RepeatCount { get; set; } = 1;

    /// <summary>The W3C trace id of the request or operation the entry was logged in (32 hex characters).</summary>
    public string? TraceId { get; set; }

    public Guid? ClinicId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }

    /// <summary>HTTP method and matched route template when logged during a request (never the raw path).</summary>
    public string? Method { get; set; }
    public string? Route { get; set; }

    /// <summary>The machine or container that logged it (several app instances can run).</summary>
    public string? Instance { get; set; }

    /// <summary>The entry's other structured values and scope values as a JSON object, secrets masked.</summary>
    public string Properties { get; set; } = "{}";
}
