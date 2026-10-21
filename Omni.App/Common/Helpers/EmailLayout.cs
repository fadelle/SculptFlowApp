using System.Net;
using System.Text;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Common.Helpers;

/// <summary>
/// The one layout of Omni's emails: a heading, paragraphs, one button and the link written out, then the footer. Inline
/// styles only (mail clients drop style sheets), no images, lang and dir from the reader's language. Every text is
/// HTML-encoded here, so callers pass plain text. The plain-text twin carries the same words and the link.
/// </summary>
public static class EmailLayout
{
    public static (string Html, string Text) Build(ReaderText text, string heading, IReadOnlyList<string> paragraphs,
        string buttonLabel, string buttonUrl, IReadOnlyList<string>? afterButton = null)
    {
        var align = text.Direction == "rtl" ? "right" : "left"; // rtl-ok: mail clients don't support logical text-align values
        var e = (string s) => WebUtility.HtmlEncode(s);
        var html = new StringBuilder();
        html.Append($"<!doctype html><html lang=\"{text.Language}\" dir=\"{text.Direction}\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append($"<title>{e(heading)}</title></head>");
        html.Append("<body style=\"margin:0;padding:24px 12px;background:#f4f5f7;font-family:-apple-system,'Segoe UI',Roboto,'Noto Sans Arabic',Arial,sans-serif;color:#1f2328;\">");
        html.Append($"<div dir=\"{text.Direction}\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px 28px;text-align:{align};\">");
        html.Append("<div style=\"font-size:20px;font-weight:700;color:#0d9488;margin-bottom:20px;\">Omni</div>");
        html.Append($"<h1 style=\"font-size:22px;line-height:1.3;margin:0 0 16px;\">{e(heading)}</h1>");
        foreach (var p in paragraphs) html.Append($"<p style=\"font-size:15px;line-height:1.6;margin:0 0 14px;\">{e(p)}</p>");
        html.Append($"<p style=\"margin:24px 0;\"><a href=\"{e(buttonUrl)}\" style=\"display:inline-block;background:#0d9488;color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;padding:12px 22px;border-radius:8px;\">{e(buttonLabel)}</a></p>");
        foreach (var p in afterButton ?? []) html.Append($"<p style=\"font-size:14px;line-height:1.6;margin:0 0 12px;color:#57606a;\">{e(p)}</p>");
        html.Append($"<p style=\"font-size:13px;line-height:1.5;margin:20px 0 4px;color:#57606a;\">{e(text["emails.common.linkFallback"])}</p>");
        html.Append($"<p dir=\"ltr\" style=\"font-size:13px;line-height:1.5;margin:0;word-break:break-all;\"><a href=\"{e(buttonUrl)}\" style=\"color:#0d9488;\">{e(buttonUrl)}</a></p>");
        html.Append($"<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:28px 0 16px;\"><p style=\"font-size:12px;line-height:1.5;color:#8c959f;margin:0;\">{e(text["emails.common.footer"])}</p>");
        html.Append("</div></body></html>");

        var plain = new StringBuilder();
        plain.AppendLine(heading).AppendLine();
        foreach (var p in paragraphs) plain.AppendLine(p).AppendLine();
        plain.AppendLine($"{buttonLabel}: {buttonUrl}").AppendLine();
        foreach (var p in afterButton ?? []) plain.AppendLine(p).AppendLine();
        plain.AppendLine("--").AppendLine(text["emails.common.footer"]);
        return (html.ToString(), plain.ToString());
    }
}
