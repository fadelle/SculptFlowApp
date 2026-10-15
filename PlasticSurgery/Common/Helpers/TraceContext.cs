using System.Diagnostics;

namespace PlasticSurgery.Common.Helpers;

/// <summary>
/// The current W3C trace (ASP.NET Core starts one per request; an incoming traceparent header continues the caller's,
/// which is how n8n's callbacks join the trace of the message that triggered them).
/// </summary>
public static class TraceContext
{
    /// <summary>The current trace id (32 hex characters), or null outside any request or traced job.</summary>
    public static string? CurrentTraceId()
    {
        var activity = Activity.Current;
        return activity is null || activity.TraceId == default ? null : activity.TraceId.ToHexString();
    }

    /// <summary>The current trace as a traceparent header value ("00-{trace}-{span}-01"), for n8n to send back.</summary>
    public static string? CurrentTraceparent()
    {
        var activity = Activity.Current;
        if (activity is null || activity.TraceId == default) return null;
        return $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01";
    }

    /// <summary>
    /// Starts a new trace (not a child of the current one) for a piece of work that is its own story, such as one
    /// campaign recipient's send inside a batch. Disposing it ends the trace and restores the previous one.
    /// </summary>
    public static IDisposable StartNew(string name)
    {
        var previous = Activity.Current;
        Activity.Current = null;
        var activity = new Activity(name).SetIdFormat(ActivityIdFormat.W3C).Start();
        return new Scope(activity, previous);
    }

    private sealed class Scope(Activity activity, Activity? previous) : IDisposable
    {
        public void Dispose()
        {
            activity.Stop();
            Activity.Current = previous;
        }
    }
}
