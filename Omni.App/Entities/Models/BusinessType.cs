namespace Omni.App.Entities.Models;

/// <summary>
/// A kind of business a client is (core.business_types): the words its dashboard uses, the AI assistant's profile and
/// the pipeline stages its contacts move through. Edited in the admin portal. The jsonb columns are kept as JSON text
/// here and read through BusinessTypeMapper: Name {"en": "..."}, Terminology {"en": {"contact": {"one", "other"}, ...}}.
/// </summary>
public class BusinessType
{
    public string Code { get; set; } = string.Empty;
    public string NameJson { get; set; } = "{}";
    public string TerminologyJson { get; set; } = "{}";
    public string AiProfile { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<PipelineStage> Stages { get; set; } = new();
}
