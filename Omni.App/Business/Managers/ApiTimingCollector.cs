using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Managers;

/// <summary>
/// Singleton. Keeps the current minute's request timings per method + route in memory. One lock guards the window:
/// recording is a dictionary lookup and a few additions, far cheaper than the request it measures, and the lock makes
/// a drain exact (no request is counted twice or lost between windows). The p95 comes from up to
/// ApiTimingLimits.MaxSamplesPerRoute durations per route (a uniform reservoir sample beyond that), so memory stays
/// bounded however busy a route is.
/// </summary>
public sealed class ApiTimingCollector : IApiTimingCollector
{
    private readonly object _gate = new();
    private Dictionary<(string Method, string Route), RouteWindow> _window = new();
    private DateTimeOffset _windowStart;

    public ApiTimingCollector(TimeProvider time)
    {
        _windowStart = time.GetUtcNow();
    }

    public void Record(string method, string route, int statusCode, double elapsedMs, bool slow)
    {
        lock (_gate)
        {
            var key = (method, route);
            if (!_window.TryGetValue(key, out var window))
            {
                if (_window.Count >= ApiTimingLimits.MaxRoutesPerWindow) key = (method, ApiTimingLimits.OtherRoute);
                if (!_window.TryGetValue(key, out window))
                {
                    window = new RouteWindow();
                    _window[key] = window;
                }
            }
            window.Add(statusCode, elapsedMs, slow);
        }
    }

    public List<ApiRouteStat> Drain(DateTimeOffset now)
    {
        Dictionary<(string Method, string Route), RouteWindow> ended;
        DateTimeOffset start;
        lock (_gate)
        {
            ended = _window;
            start = _windowStart;
            _window = new Dictionary<(string Method, string Route), RouteWindow>();
            _windowStart = now;
        }

        var bucket = ToNearestMinute(start);
        return ended.Select(pair => pair.Value.ToStat(bucket, pair.Key.Method, pair.Key.Route)).ToList();
    }

    // The flush job drains on whole minutes, so a window starts a few milliseconds after hh:mm:00; rounding names it
    // by that minute. (The first window after start-up is partial; it is named by the minute it started nearest to.)
    private static DateTimeOffset ToNearestMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var floor = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        return utc - floor >= TimeSpan.FromSeconds(30) ? floor.AddMinutes(1) : floor;
    }

    /// <summary>One route's counters for the current window (only touched under the collector's lock).</summary>
    private sealed class RouteWindow
    {
        private readonly List<double> _samples = new();
        private int _count;
        private int _errors;
        private int _clientErrors;
        private int _slow;
        private double _totalMs;
        private double _maxMs;

        public void Add(int statusCode, double elapsedMs, bool slow)
        {
            _count++;
            if (statusCode >= 500) _errors++;
            else if (statusCode >= 400) _clientErrors++;
            if (slow) _slow++;
            _totalMs += elapsedMs;
            if (elapsedMs > _maxMs) _maxMs = elapsedMs;

            if (_samples.Count < ApiTimingLimits.MaxSamplesPerRoute) _samples.Add(elapsedMs);
            else
            {
                // Reservoir sampling: every request so far has the same chance of being in the sample.
                var slot = Random.Shared.Next(_count);
                if (slot < _samples.Count) _samples[slot] = elapsedMs;
            }
        }

        public ApiRouteStat ToStat(DateTimeOffset bucket, string method, string route) => new()
        {
            BucketStart = bucket,
            Method = method,
            Route = route,
            RequestCount = _count,
            ErrorCount = _errors,
            ClientErrorCount = _clientErrors,
            SlowCount = _slow,
            TotalMs = Math.Round(_totalMs, 3),
            P95Ms = Math.Round(Percentile95(_samples), 3),
            MaxMs = Math.Round(_maxMs, 3)
        };

        // Nearest-rank 95th percentile.
        private static double Percentile95(List<double> samples)
        {
            if (samples.Count == 0) return 0;
            samples.Sort();
            var rank = (int)Math.Ceiling(0.95 * samples.Count);
            return samples[Math.Clamp(rank, 1, samples.Count) - 1];
        }
    }
}
