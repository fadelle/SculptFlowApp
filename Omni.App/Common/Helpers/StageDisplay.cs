using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Common.Helpers;

/// <summary>
/// How a contact's stage looks on screen: its label from the client's business type, and a badge colour from its category
/// (so every business type's stages read the same way; wwwroot/js/inbox.js mirrors the colours). A key that isn't one of
/// the client's stages (data from before a business-type change) shows as the key made readable, in gray.
/// </summary>
public static class StageDisplay
{
    private static readonly Dictionary<string, string> CategoryColors = new(StringComparer.Ordinal)
    {
        [StageCategory.Open] = "badge-blue",
        [StageCategory.Booked] = "badge-amber",
        [StageCategory.Attended] = "badge-green",
        [StageCategory.NoShow] = "badge-red",
        [StageCategory.Converted] = "badge-teal",
        [StageCategory.ClosedLost] = "badge-gray",
        [StageCategory.NeedsHuman] = "badge-red",
    };

    public static string Label(IReadOnlyList<PipelineStageOption> stages, string? key) =>
        Find(stages, key)?.Label ?? FilterLabelHelper.Label(key);

    public static string CssClass(IReadOnlyList<PipelineStageOption> stages, string? key) =>
        Find(stages, key) is { } stage ? CategoryColors.GetValueOrDefault(stage.Category, "badge-gray") : "badge-gray";

    private static PipelineStageOption? Find(IReadOnlyList<PipelineStageOption> stages, string? key) =>
        key is null ? null : stages.FirstOrDefault(s => s.Key == key);
}
