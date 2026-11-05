using Microsoft.Extensions.Localization;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Why Meta rejected or stopped a WhatsApp template, as stored in whatsapp_templates.rejection_reason (QA t10-06): Meta's code
/// on the first line (INVALID_FORMAT, PROMOTIONAL...) and Meta's own explanation and recommendation after it, or just one of
/// the two. Shown with the code translated (templates.reason.{camelCase}) and Meta's words as Meta wrote them.
/// </summary>
public static class TemplateReasons
{
    public const string LabelKeyPrefix = "templates.reason.";

    /// <summary>Meta's documented rejection codes; each has a label.</summary>
    public static readonly IReadOnlyList<string> Codes =
        ["ABUSIVE_CONTENT", "INVALID_FORMAT", "PROMOTIONAL", "TAG_CONTENT_MISMATCH", "SCAM", "INCORRECT_CATEGORY"];

    /// <summary>The stored text for a code and Meta's explanation; null when there is neither ("NONE" is none).</summary>
    public static string? Compose(string? code, string? detail)
    {
        var c = string.IsNullOrWhiteSpace(code) || string.Equals(code.Trim(), "NONE", StringComparison.OrdinalIgnoreCase) ? null : code.Trim();
        var d = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        return c is null ? d : d is null ? c : $"{c}\n{d}";
    }

    /// <summary>The reason in the reader's language: a known code becomes its label, Meta's explanation follows as written.</summary>
    public static string? Describe(IStringLocalizer text, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || string.Equals(stored.Trim(), "NONE", StringComparison.OrdinalIgnoreCase)) return null;
        var (code, detail) = Split(stored);
        if (code is null) return stored.Trim();
        var label = text[LabelKey(code)].Value;
        return detail is null ? label : $"{label} {detail}";
    }

    /// <summary>A notification argument for the reason: the code's label (translated when read) or the stored text.</summary>
    public static object Arg(string? stored)
    {
        var (code, detail) = Split(stored ?? "");
        return code is not null && detail is null ? TextArg.Of(LabelKey(code)) : (object)(stored ?? "");
    }

    public static string LabelKey(string code) => LabelKeyPrefix + FilterLabelHelper.KeyPart(code);

    /// <summary>The known code on the first line (null when the first line isn't one), and the rest.</summary>
    private static (string? Code, string? Detail) Split(string stored)
    {
        var lines = stored.Trim().Split('\n', 2);
        var first = lines[0].Trim();
        var known = Codes.Contains(first.ToUpperInvariant());
        var rest = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : null;
        return known ? (first.ToUpperInvariant(), rest) : (null, null);
    }
}
