using System.Diagnostics;
using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Managers;

/// <summary>Singleton. One lock guards a bounded queue; a step is a few field copies and a small JSON serialization.</summary>
public sealed class MessageTracer : IMessageTracer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IConfigManager _config;
    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Queue<MessageTraceStep> _queue = new();
    private long _dropped;

    public MessageTracer(IConfigManager config, IHttpContextAccessor http, TimeProvider time)
    {
        _config = config;
        _http = http;
        _time = time;
    }

    public void Step(string step, string outcome, TraceIds ids, double? durationMs = null, object? detail = null)
    {
        try
        {
            if (!_config.MessageTraceEnabled) return;
            var row = new MessageTraceStep
            {
                // Outside a request or traced job there is no trace; the step still gets an id of its own.
                TraceId = TraceContext.CurrentTraceId() ?? ActivityTraceId.CreateRandom().ToHexString(),
                OccurredAt = _time.GetUtcNow(),
                Step = step,
                Outcome = outcome,
                DurationMs = durationMs is { } ms ? Math.Round(ms, 1) : null,
                ClientId = ids.ClientId,
                ConversationId = ids.ConversationId,
                MessageId = ids.MessageId,
                Channel = TraceText.Cap(ids.Channel, 50),
                ExternalMessageId = TraceText.Cap(ids.ExternalMessageId, 200),
                N8nExecutionId = TraceText.Cap(_http.HttpContext?.Request.Headers[MessageTraceLimits.N8nExecutionHeader].FirstOrDefault(),
                    MessageTraceLimits.MaxExecutionIdLength),
                Detail = DetailJson(detail),
                Instance = AppLogLimits.InstanceName
            };
            Enqueue(row);
        }
        catch
        {
            // Tracing must never break the message it describes.
        }
    }

    public List<MessageTraceStep> Drain(int max)
    {
        lock (_gate)
        {
            var batch = new List<MessageTraceStep>(Math.Min(max, _queue.Count));
            while (batch.Count < max && _queue.TryDequeue(out var step)) batch.Add(step);
            return batch;
        }
    }

    public void Requeue(IEnumerable<MessageTraceStep> steps)
    {
        foreach (var step in steps) Enqueue(step);
    }

    public int Pending
    {
        get
        {
            lock (_gate) return _queue.Count;
        }
    }

    public long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);

    private void Enqueue(MessageTraceStep step)
    {
        lock (_gate)
        {
            if (_queue.Count >= MessageTraceLimits.BufferCapacity)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }
            _queue.Enqueue(step);
        }
    }

    private static string DetailJson(object? detail)
    {
        if (detail is null) return "{}";
        var json = JsonSerializer.Serialize(detail, Json);
        return json.Length <= MessageTraceLimits.MaxDetailLength && json.StartsWith('{') ? json : "{\"_truncated\":true}";
    }
}