using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Entities.Requests.BusinessTypes;

/// <summary>
/// Create (POST, Code required) or replace (PUT, Code ignored: it never changes) a business type. Name and Terminology
/// are per language ("en" required and complete); Stages is the whole pipeline in order: a key not listed any more is
/// removed (refused while contacts are in it), a new key is added. Every category needs at least one stage.
/// </summary>
public record SaveBusinessTypeRequest(
    string? Code,
    Dictionary<string, string>? Name,
    Dictionary<string, Dictionary<string, TermForms>>? Terminology,
    string? AiProfile,
    int SortOrder,
    List<SaveStageRequest>? Stages);
