using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Entities.Responses.BusinessTypes;

/// <summary>A business type for the admin portal: every language it has, its stages in pipeline order, and how many
/// clients use it.</summary>
public record BusinessTypeResponse(
    string Code,
    IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, TermForms>> Terminology,
    string AiProfile,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<PipelineStageResponse> Stages,
    int ClientCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
