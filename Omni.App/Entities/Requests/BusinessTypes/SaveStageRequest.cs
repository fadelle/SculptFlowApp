namespace Omni.App.Entities.Requests.BusinessTypes;

/// <summary>One stage in <see cref="SaveBusinessTypeRequest.Stages"/>; its position in the list is its pipeline order.</summary>
public record SaveStageRequest(string? Key, string? Category, Dictionary<string, string>? Labels);
