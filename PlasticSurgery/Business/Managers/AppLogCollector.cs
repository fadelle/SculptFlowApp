using Microsoft.Extensions.Logging;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Monitoring;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Managers;

/// <summary>
/// Singleton. Holds captured log entries until AppLogFlushJob writes them. One lock guards the queue and the
/// per-fingerprint minute counters; adding is a dictionary lookup and an enqueue, far cheaper than the logging call.
/// </summary>
public sealed class AppLogCollector : IAppLogCollector
{
    private static readonly AsyncLocal<bool> Suppressed = new();

    private readonly object _gate = new();
    private readonly Queue<AppLog> _queue = new();
    private readonly Dictionary<string, FingerprintMinute> _minutes = new(StringComparer.Ordinal);
    private long _dropped;
    private DateTimeOffset _lastDroppedAt;
    private volatile AppLogCaptureSettings _settings = AppLogCaptureSettings.Default;

    public AppLogCaptureSettings Settings => _settings;

    public void Apply(AppLogCaptureSettings settings) => _settings = settings;

    public bool ShouldCapture(LogLevel level, string category)
    {
        if (Suppressed.Value || level == LogLevel.None) return false;
        var settings = _settings;
        if (!settings.Enabled || level < settings.MinimumLevel) return false;
        foreach (var prefix in settings.ExcludedCategoryPrefixes)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public void Add(AppLog entry)
    {
        var minute = MinuteOf(entry.OccurredAt);
        var cap = _settings.MaxPerFingerprintPerMinute;
        lock (_gate)
        {
            if (_minutes.TryGetValue(entry.Fingerprint, out var counter))
            {
                if (counter.Minute != minute)
                {
                    Close(counter);
                    counter.Minute = minute;
                    counter.Count = 0;
                }
            }
            else if (_minutes.Count < AppLogLimits.MaxTrackedFingerprints)
            {
                counter = new FingerprintMinute { Minute = minute };
                _minutes[entry.Fingerprint] = counter;
            }

            if (counter is not null)
            {
                if (counter.Count >= cap)
                {
                    counter.Suppressed += entry.RepeatCount;
                    counter.LastSuppressed = entry;
                    return;
                }
                counter.Count++;
            }
            Enqueue(entry);
        }
    }

    public void Requeue(IEnumerable<AppLog> entries)
    {
        lock (_gate)
        {
            foreach (var entry in entries) Enqueue(entry);
        }
    }

    public List<AppLog> Drain(DateTimeOffset now, int max)
    {
        var current = MinuteOf(now);
        lock (_gate)
        {
            foreach (var (fingerprint, counter) in _minutes.ToList())
            {
                if (counter.Minute >= current) continue;
                Close(counter);
                _minutes.Remove(fingerprint);
            }

            if (_dropped > 0 && _queue.Count < AppLogLimits.BufferCapacity)
            {
                _queue.Enqueue(DroppedEntry(_dropped, _lastDroppedAt));
                _dropped = 0;
            }

            var batch = new List<AppLog>(Math.Min(max, _queue.Count));
            while (batch.Count < max && _queue.TryDequeue(out var entry)) batch.Add(entry);
            return batch;
        }
    }

    public int Pending
    {
        get
        {
            lock (_gate) return _queue.Count;
        }
    }

    public IDisposable Suppress()
    {
        var previous = Suppressed.Value;
        Suppressed.Value = true;
        return new Restore(previous);
    }

    // Under the lock: the minute's suppressed entries become one row standing for all of them (the last one's text).
    private void Close(FingerprintMinute counter)
    {
        if (counter.Suppressed == 0 || counter.LastSuppressed is null) return;
        counter.LastSuppressed.RepeatCount = (int)Math.Min(counter.Suppressed, int.MaxValue);
        Enqueue(counter.LastSuppressed);
        counter.Suppressed = 0;
        counter.LastSuppressed = null;
    }

    // Under the lock.
    private void Enqueue(AppLog entry)
    {
        if (_queue.Count >= AppLogLimits.BufferCapacity)
        {
            _dropped += entry.RepeatCount;
            _lastDroppedAt = entry.OccurredAt;
            return;
        }
        _queue.Enqueue(entry);
    }

    private static AppLog DroppedEntry(long count, DateTimeOffset at)
    {
        const string template = "{Count} log entries were not saved because more than {Capacity} were waiting to be written.";
        return new AppLog
        {
            OccurredAt = at,
            Level = AppLogLevels.Warning,
            Category = AppLogLimits.DroppedCategory,
            Message = $"{count} log entries were not saved because more than {AppLogLimits.BufferCapacity} were waiting to be written.",
            MessageTemplate = template,
            Fingerprint = AppLogFingerprint.Compute(AppLogLimits.DroppedCategory, template, null, null),
            RepeatCount = (int)Math.Min(count, int.MaxValue),
            Instance = AppLogLimits.InstanceName
        };
    }

    private static DateTimeOffset MinuteOf(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>One fingerprint's entries in its current minute (only touched under the collector's lock).</summary>
    private sealed class FingerprintMinute
    {
        public DateTimeOffset Minute;
        public int Count;
        public long Suppressed;
        public AppLog? LastSuppressed;
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Suppressed.Value = previous;
    }
}
