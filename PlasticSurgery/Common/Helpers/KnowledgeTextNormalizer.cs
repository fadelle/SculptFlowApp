using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PlasticSurgery.Common.Helpers;

/// <summary>Cleans extracted text: consistent newlines, no control/zero-width characters, collapsed
/// whitespace, and at most one blank line between paragraphs. Paragraph breaks are preserved because
/// the chunker splits on them.</summary>
public static partial class KnowledgeTextNormalizer
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        raw = raw.Replace("\r\n", "\n"); // so a Windows line ending is one newline, not a blank line
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '\r': sb.Append('\n'); break;
                case '\f': sb.Append("\n\n"); break;           // form feed = page break
                case '\t': case ' ': sb.Append(' '); break; // tabs / non-breaking space
                case '​': case '‌': case '‍': case '⁠': case '﻿': case '­': break; // invisible
                case '\n': sb.Append('\n'); break;
                default:
                    if (!char.IsControl(ch)) sb.Append(ch);
                    break;
            }
        }

        var text = SpaceRuns().Replace(sb.ToString(), " ");
        text = LineEdges().Replace(text, "\n");     // trim spaces around newlines
        text = ManyNewlines().Replace(text, "\n\n"); // at most one blank line
        return text.Trim();
    }

    [GeneratedRegex(" {2,}")] private static partial Regex SpaceRuns();
    [GeneratedRegex(@" *\n *")] private static partial Regex LineEdges();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex ManyNewlines();
}
