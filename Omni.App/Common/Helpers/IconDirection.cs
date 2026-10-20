namespace Omni.App.Common.Helpers;

/// <summary>Icons that point along the reading direction (docs/UI_GUIDE.md §0.4). In a right-to-left page they mirror
/// through the <c>au-icon--dir</c> class, so "back" points right and "next" points left in Arabic.
/// <c>Guards/RtlStyleTests</c> fails when markup shows one of these glyphs without the class.</summary>
public static class IconDirection
{
    /// <summary>The directional glyphs. Add a new directional icon here and give its markup <c>au-icon--dir</c>.</summary>
    public static readonly IReadOnlyCollection<string> Glyphs = new HashSet<string>(StringComparer.Ordinal)
    {
        "arrow_back", "arrow_forward", "chevron_left", "chevron_right", "send", "login", "logout", "undo", "redo",
    };

    /// <summary>The class list for an icon chosen at run time: "au-icon au-icon--dir" for a directional glyph, else
    /// "au-icon".</summary>
    public static string ClassFor(string? glyph) =>
        glyph is not null && Glyphs.Contains(glyph) ? "au-icon au-icon--dir" : "au-icon";
}
