using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PlasticSurgery.Business.Contracts.Engines.Knowledge;

/// <summary>
/// Turns an uploaded file into clean plain text for the Knowledge Base. Text-based documents only —
/// there is deliberately NO OCR, so a scanned PDF (images only) is reported as unreadable rather than
/// silently producing an empty document.
///
/// Supported: PDF (PdfPig), DOCX (Open XML SDK), TXT. Anything else — including legacy .doc — is rejected.
/// The type is decided by extension AND verified against the file's leading bytes (a renamed .exe is not
/// a PDF), never by the browser-supplied Content-Type.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>Extracts and normalizes the text of a PDF/DOCX/TXT. Throws <see cref="InvalidDocumentException"/>
    /// (an ArgumentException) with a message safe to show to staff for every user-fixable problem.</summary>
    Task<string> ExtractAsync(Stream content, string fileName, CancellationToken ct = default);
}
