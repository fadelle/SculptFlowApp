namespace PlasticSurgery.Common.Statics;

/// <summary>activity.message_trace_steps.outcome: the step did its job, was deliberately not done, or went wrong.</summary>
public static class TraceOutcomes
{
    public const string Ok = "ok";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}