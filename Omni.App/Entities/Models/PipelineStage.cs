namespace Omni.App.Entities.Models;

/// <summary>One stage of a business type's pipeline (crm.pipeline_stages). Contacts store the key; code, reports and the
/// AI read the category (<see cref="Common.Enums.StageCategory"/>); people see the label in their language
/// (LabelsJson {"en": "...", "ar": "..."}).</summary>
public class PipelineStage
{
    public string BusinessTypeCode { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LabelsJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
