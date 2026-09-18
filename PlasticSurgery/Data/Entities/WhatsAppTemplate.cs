namespace PlasticSurgery.Data.Entities;

/// <summary>
/// A WhatsApp message template — required by Meta for any outbound message sent outside the
/// 24-hour customer service window (see Conversation.ServiceWindowExpiresAt). Created here as a
/// draft first, then submitted to Meta for approval — see WhatsAppTemplateService.
/// </summary>
public class WhatsAppTemplate
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    /// <summary>Meta's own template id, once submitted — null while still a local draft.</summary>
    public string? MetaTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = WhatsAppTemplateCategory.Utility;
    public string Language { get; set; } = "en_US";
    /// <summary>Not DB-CHECK-constrained (see schema.sql) — Meta's own template statuses aren't a
    /// guaranteed-stable set, so this tolerates values beyond WhatsAppTemplateStatus's known list.
    /// The create-template flow (our own UI) still only ever writes a known value.</summary>
    public string Status { get; set; } = WhatsAppTemplateStatus.Draft;

    public string? HeaderType { get; set; }
    public string? HeaderContent { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Footer { get; set; }

    /// <summary>Raw JSON (Meta's button array shape) — the "buttons_json" jsonb column.</summary>
    public string? ButtonsJson { get; set; }
    /// <summary>Raw JSON describing this template's {{1}}, {{2}}... placeholders — the "variables_json" jsonb column.</summary>
    public string? VariablesJson { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Which clinic's WhatsApp connection this template belongs to — nullable because a
    /// template can exist as a local draft before any connection/WABA is involved.</summary>
    public Guid? ChannelIntegrationId { get; set; }

    /// <summary>Meta's quality rating for this template (e.g. "green"/"yellow"/"red"), from
    /// template-status webhook events. Free text — see the class doc's note on Status.</summary>
    public string? QualityRating { get; set; }
    public string? PreviousCategory { get; set; }
    public string? CurrentCategory { get; set; }

    /// <summary>Raw Meta "components" array (header/body/footer/buttons) as last reported by a
    /// webhook — kept verbatim alongside our own split Header/Body/Footer/Buttons fields (which
    /// drive the create-template UI) rather than replacing them.</summary>
    public string? ComponentsJson { get; set; }

    public DateTimeOffset? LastMetaEventAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
}

public static class WhatsAppTemplateCategory
{
    public const string Marketing = "marketing";
    public const string Utility = "utility";
    public const string Authentication = "authentication";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Marketing, Utility, Authentication };
}

/// <summary>Mirrors Meta's WhatsApp template review statuses — the ones our own create-template UI
/// writes. Not exhaustive of what Meta can send via webhook (see Status's doc comment); the
/// badge-mapping in Pages/Shared/StatusBadgeHelper.cs falls back to a generic "Problem" bucket for
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

public static class WhatsAppTemplateHeaderType
{
    public const string None = "none";
    public const string Text = "text";
    public const string Image = "image";
    public const string Video = "video";
    public const string Document = "document";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { None, Text, Image, Video, Document };
}
