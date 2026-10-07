using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace PlasticSurgery.Entities.Dtos.WebScraping;

public sealed record ExtractedPage(
    string? Title,
    /// <summary>The raw href of rel=canonical, resolved to absolute (not yet normalized/validated).</summary>
    string? CanonicalHref,
    bool NoIndex,
    bool NoFollow,
    /// <summary>Absolute hrefs of every followable link on the page.</summary>
    IReadOnlyList<string> Links,
    IReadOnlyList<TextBlock> Blocks);
