using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>An uploaded document that can't be used — unsupported/mismatched type, unreadable, or with no
/// text. Derives from ArgumentException so the existing Knowledge Base callers already turn it into a
/// 400 / form error.</summary>
public class InvalidDocumentException : ArgumentException
{
    public InvalidDocumentException(string message) : base(message) { }
}
