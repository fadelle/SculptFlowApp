namespace Omni.App.Entities.Responses.BusinessTypes;

public record PipelineStageResponse(string Key, string Category, IReadOnlyDictionary<string, string> Labels, int SortOrder);
