namespace Omni.App.Entities.Dtos.BusinessTypes;

/// <summary>One stage on Settings → Contact stages (QA t12-fix-06): the type's key, category and labels, and the client's own
/// labels (only the languages it set).</summary>
public sealed record ClientStageLabelRow(string Key, string Category, IReadOnlyDictionary<string, string> TypeLabels,
    IReadOnlyDictionary<string, string> OwnLabels);
