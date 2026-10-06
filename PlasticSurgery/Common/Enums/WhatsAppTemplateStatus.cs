namespace PlasticSurgery.Common.Enums;

/// <summary>Mirrors Meta's WhatsApp template review statuses — the ones our own create-template UI
/// writes. Not exhaustive of what Meta can send via webhook (see Status's doc comment); the
/// badge-mapping in Common/Helpers/StatusBadgeHelper.cs falls back to a generic "Problem" bucket for
/// anything not listed here (e.g. "flagged", "deleted", "in_appeal").</summary>
public static class WhatsAppTemplateStatus
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Paused = "paused";
    public const string Disabled = "disabled";
    public const string Flagged = "flagged";
    public const string Deleted = "deleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Draft, Pending, Approved, Rejected, Paused, Disabled, Flagged, Deleted
    };
}
