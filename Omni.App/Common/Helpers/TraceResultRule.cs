using System.Text.Json;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.PlatformAdmin;

namespace Omni.App.Common.Helpers;

/// <summary>A trace's outcome (TraceResults) and its reason, from its steps (QA t7-10). In order: the conversation went to
/// the team (even if a handoff message went out too); a reply went out; something failed; it was skipped on purpose (the reason from the step's detail, else the
/// step); the bot was asked and nothing came of it (still in progress for a few minutes); the message was only saved; other.</summary>
public static class TraceResultRule
{
    /// <summary>How long after its last step a trace whose bot was asked still counts as in progress.</summary>
    public static readonly TimeSpan InProgressWindow = TimeSpan.FromMinutes(5);

    public static (string Outcome, string? Reason) Of(TraceFacts facts, DateTimeOffset now)
    {
        if (facts.ModeChanged > 0 || facts.HandoffAction) return (TraceResults.HandedOff, null);
        if (facts.Accepted > 0) return (TraceResults.Replied, null);
        if (facts.FailedStep is not null) return (TraceResults.Failed, facts.FailedStep);
        if (facts.SkipStep is not null) return (TraceResults.Skipped, ReasonOf(facts.SkipDetail) ?? facts.SkipStep);
        if (facts.Triggered > 0) return (now - facts.EndedAt < InProgressWindow ? TraceResults.InProgress : TraceResults.NoReply, null);
        if (facts.Saved > 0) return (TraceResults.Received, null);
        return (TraceResults.Other, null);
    }

    private static string? ReasonOf(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        try
        {
            using var doc = JsonDocument.Parse(detail);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("reason", out var reason)
                && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
