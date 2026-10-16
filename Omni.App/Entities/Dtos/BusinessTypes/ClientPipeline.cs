using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.BusinessTypes;

/// <summary>
/// One client's pipeline: its business type's stages in pipeline order, labelled in the request's language. Code moves
/// contacts through it by stage <see cref="StageCategory">category</see>, never by one type's stage keys: every type has at
/// least one stage in each category (BusinessTypeService validates that), so a category always resolves to a stage.
/// </summary>
public sealed record ClientPipeline(IReadOnlyList<PipelineStageOption> Stages)
{
    public static readonly ClientPipeline Empty = new([]);

    /// <summary>Where a new contact starts: the first open stage (the first stage when the type has no open one).</summary>
    public string EntryKey =>
        (FirstOf(StageCategory.Open) ?? Stages.FirstOrDefault())?.Key
        ?? throw new InvalidOperationException("The client has no pipeline stages.");

    public PipelineStageOption? Find(string? key) => string.IsNullOrEmpty(key) ? null : Stages.FirstOrDefault(s => s.Key == key);

    public string? CategoryOf(string? key) => Find(key)?.Category;

    public PipelineStageOption? FirstOf(string category) => Stages.FirstOrDefault(s => s.Category == category);

    /// <summary>Every stage key in any of <paramref name="categories"/>.</summary>
    public IReadOnlyList<string> KeysOf(params string[] categories) =>
        Stages.Where(s => categories.Contains(s.Category)).Select(s => s.Key).ToList();

    /// <summary>The stage a contact in <paramref name="currentKey"/> moves to for <paramref name="category"/>: the current stage
    /// when it already has that category (a type may have several, e.g. "Not interested" and "Lost"), else the category's
    /// first stage. Null only for a category the type lacks.</summary>
    public string? MoveTo(string? currentKey, string category) =>
        CategoryOf(currentKey) == category ? currentKey : FirstOf(category)?.Key;

    /// <summary>A stage key of this pipeline, or a category name (resolved to the category's first stage); a key wins when a
    /// key and a category share a name. Null when it is neither.</summary>
    public string? Resolve(string? keyOrCategory)
    {
        var value = keyOrCategory?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return Find(value)?.Key ?? (StageCategory.All.Contains(value) ? FirstOf(value)?.Key : null);
    }

    /// <summary>The open stage right after <paramref name="key"/> when <paramref name="key"/> is the entry stage (the first
    /// reply moves a contact from "new" to "contacted" in both built-in types); null when there is none.</summary>
    public string? NextAfterEntry(string? key)
    {
        if (Stages.Count == 0 || key != EntryKey) return null;
        var open = Stages.Where(s => s.Category == StageCategory.Open).ToList();
        var index = open.FindIndex(s => s.Key == key);
        return index >= 0 && index + 1 < open.Count ? open[index + 1].Key : null;
    }
}
