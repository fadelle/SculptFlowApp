namespace Omni.App.Entities.Models;

/// <summary>A client's own label for one stage of its business type (crm.client_stage_labels, QA t12-fix-06). The key and the
/// category stay the type's; LabelsJson {"en": "...", "ar": "..."} replaces the type's label in those languages, for this client only.</summary>
public class ClientStageLabel
{
    public Guid ClientId { get; set; }
    public string StageKey { get; set; } = string.Empty;
    public string LabelsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
