namespace Omni.App.Entities.Dtos.BusinessTypes;

/// <summary>A stage of the signed-in client's pipeline, labelled in the user's language (filters, badges, dropdowns).</summary>
public record PipelineStageOption(string Key, string Category, string Label);
