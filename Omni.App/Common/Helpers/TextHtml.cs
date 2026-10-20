using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Localization;

namespace Omni.App.Common.Helpers;

/// <summary>
/// A UI string with HTML inside it, for Razor: <c>@T.Html("billing.pastDue.text", LocalTimeHelper.Time(at, "date"))</c>.
/// The key's text is HTML-encoded; an <see cref="IHtmlContent"/> argument (a &lt;time&gt; tag, a link) goes in as HTML at its
/// {0}, any other argument is encoded like the text. Business-type terms are filled in as usual.
/// </summary>
public static class TextHtml
{
    private const char Open = '\u0001';
    private const char Close = '\u0002';

    public static IHtmlContent Html(this IStringLocalizer text, string key, params object?[] args)
    {
        // Each argument becomes a marker the strings file can't contain, then the text is cut at the markers.
        var markers = args.Select((_, i) => (object?)$"{Open}{i}{Close}").ToArray();
        var filled = text[key, markers!].Value;
        var html = new HtmlContentBuilder();
        var position = 0;
        while (position < filled.Length)
        {
            var start = filled.IndexOf(Open, position);
            var end = start < 0 ? -1 : filled.IndexOf(Close, start);
            if (start < 0 || end < 0 || !int.TryParse(filled.AsSpan(start + 1, end - start - 1), out var index) || index >= args.Length)
            {
                html.Append(filled[position..]);
                break;
            }
            html.Append(filled[position..start]);
            switch (args[index])
            {
                case IHtmlContent content: html.AppendHtml(content); break;
                case { } value: html.Append(Convert.ToString(value, (text as Business.Contracts.Managers.ITextLanguage)?.Culture ?? TextCulture.Current)); break;
            }
            position = end + 1;
        }
        return html;
    }
}
