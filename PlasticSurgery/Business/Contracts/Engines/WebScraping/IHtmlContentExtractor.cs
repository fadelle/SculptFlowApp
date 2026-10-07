using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using PlasticSurgery.Entities.Dtos.WebScraping;

namespace PlasticSurgery.Business.Contracts.Engines.WebScraping;

/// <summary>
/// HTML → readable text using a real HTML5 parser (AngleSharp) — never regex on markup. Picks the main
/// content region (main / [role=main] / article / known content containers, else body), drops everything that
/// isn't human-readable page content (script, style, hidden/aria-hidden nodes, nav, header, aside, forms
/// controls, cookie/consent/popup/menu/breadcrumb/share widgets) and keeps structure: headings, paragraphs,
/// lists ("- "), table rows ("a | b"), FAQ/details text, image alt text. Footer text is dropped EXCEPT lines
/// that look like contact details (phone, email, address, hours) so that information isn't lost; repeated
/// site-wide blocks are then removed across pages by the processor.
/// </summary>
public interface IHtmlContentExtractor
{
    ExtractedPage Extract(string html, Uri pageUrl);
}
