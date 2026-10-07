using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace PlasticSurgery.Entities.Dtos.WebScraping;

/// <summary>One paragraph-level piece of a page's readable text. List items are flagged so they render as a list.</summary>
public sealed record TextBlock(string Text, bool ListItem = false);
