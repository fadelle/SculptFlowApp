namespace Omni.App.Common.Enums;

/// <summary>Why <c>IAvailabilityService.CheckSlotAsync</c> refused a time (<c>SlotCheck.Reason</c>). The AI reads the
/// English Error; the dashboard reads the reason (QA t5-ops-01): a member may book past everything but an overlap.</summary>
public static class SlotCheckReason
{
    public const string NotConfigured = "not_configured";
    public const string EndBeforeStart = "end_before_start";
    public const string Past = "past";
    public const string Notice = "notice";
    public const string Horizon = "horizon";
    public const string Closed = "closed";
    /// <summary>Overlaps another booking, or only its buffer.</summary>
    public const string Overlap = "overlap";
}
