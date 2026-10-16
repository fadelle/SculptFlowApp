using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Singleton, in memory. Records the steps a message takes (webhook in, inbox, bot, send) under the current request's
/// W3C trace id; MessageTraceFlushJob writes them to activity.message_trace_steps every few seconds. Recording is a
/// queue write: it never touches the database, never throws and never slows the message down. Off while
/// MessageTrace:Enabled is false.
/// </summary>
public interface IMessageTracer
{
    /// <summary>
    /// Records one step (a TraceSteps name, a TraceOutcomes value). <paramref name="detail"/> is a small object of facts
    /// (reasons, codes, counts), serialized to JSON; never pass message text, phone numbers or tokens.
    /// </summary>
    void Step(string step, string outcome, TraceIds ids, double? durationMs = null, object? detail = null);

    /// <summary>Up to <paramref name="max"/> waiting steps, oldest first.</summary>
    List<MessageTraceStep> Drain(int max);

    /// <summary>Puts back steps a failed write returned (beyond the buffer they are dropped and counted).</summary>
    void Requeue(IEnumerable<MessageTraceStep> steps);

    /// <summary>Steps waiting to be written.</summary>
    int Pending { get; }

    /// <summary>How many steps were dropped (buffer full) since the last call, resetting the count.</summary>
    long TakeDropped();
}