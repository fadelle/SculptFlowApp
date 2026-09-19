using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PlasticSurgery.Services;

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

/// <summary>An uploaded document that can't be used — unsupported/mismatched type, unreadable, or with no
/// text. Derives from ArgumentException so the existing Knowledge Base callers already turn it into a
/// 400 / form error.</summary>
public class InvalidDocumentException : ArgumentException
{
    public InvalidDocumentException(string message) : base(message) { }
}

public class DocumentTextExtractor : IDocumentTextExtractor
{
    public static readonly IReadOnlyList<string> SupportedExtensions = new[] { ".pdf", ".docx", ".txt" };

    /// <summary>MIME type recorded on the document, derived from the (validated) extension.</summary>
    public static string MimeTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    private readonly int _maxChars;

    public DocumentTextExtractor(IConfiguration configuration)
    {
        // A safety valve against huge documents (embedding cost) and decompression bombs (a tiny DOCX
        // that expands to gigabytes) — extraction stops as soon as the text exceeds this.
        _maxChars = Math.Max(1_000, configuration.GetValue("Knowledge:MaxExtractedChars", 250_000));
    }

    public Task<string> ExtractAsync(Stream content, string fileName, CancellationToken ct = default) =>
        // CPU-bound parsing — keep it off the request's synchronization context.
        Task.Run(() => Extract(content, fileName, ct), ct);

    private string Extract(Stream content, string fileName, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            var shown = string.IsNullOrEmpty(extension) ? "(no extension)" : extension;
            throw new InvalidDocumentException($"Unsupported file type {shown}. Upload a PDF, DOCX or TXT file.");
        }

        // Parsers want a seekable stream, and the size is already capped by the caller.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        if (buffer.Length == 0)
        {
            throw new InvalidDocumentException("The file is empty.");
        }
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        string raw = extension switch
        {
            ".pdf" => ExtractPdf(buffer, bytes, ct),
            ".docx" => ExtractDocx(buffer, bytes, ct),
            _ => ExtractText(bytes)
        };

        var text = KnowledgeTextNormalizer.Normalize(raw);
        if (text.Length == 0)
        {
            throw new InvalidDocumentException(extension switch
            {
                ".pdf" => "This PDF does not contain readable text. Scanned documents are not supported yet.",
                ".docx" => "This document does not contain any readable text.",
                _ => "The file is empty."
            });
        }
        if (text.Length > _maxChars)
        {
            throw new InvalidDocumentException(
                $"This document has too much text ({text.Length:N0} characters; the limit is {_maxChars:N0}). Split it into smaller documents.");
        }
        return text;
    }

    private string ExtractPdf(MemoryStream buffer, ReadOnlySpan<byte> bytes, CancellationToken ct)
    {
        // "%PDF-" must appear near the start (the spec allows a little leading junk).
        var head = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, 1024)]);
        if (!head.Contains("%PDF-", StringComparison.Ordinal))
        {
            throw new InvalidDocumentException("This file isn't a valid PDF (its contents don't match the .pdf extension).");
        }

        var sb = new StringBuilder();
        try
        {
            buffer.Position = 0;
            using var pdf = PdfDocument.Open(buffer);
            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                // Content-order extraction keeps reading order and line breaks (columns, headings).
                sb.Append(ContentOrderTextExtractor.GetText(page)).Append("\n\n");
                if (sb.Length > _maxChars * 2) break; // normalization will shrink it; the real cap is applied after
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidDocumentException("Couldn't read this PDF. It may be corrupted or password-protected.");
        }
        return sb.ToString();
    }

    private string ExtractDocx(MemoryStream buffer, ReadOnlySpan<byte> bytes, CancellationToken ct)
    {
        // A .docx is a zip archive: "PK\x03\x04".
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B || bytes[2] != 0x03 || bytes[3] != 0x04)
        {
            throw new InvalidDocumentException("This file isn't a valid DOCX (its contents don't match the .docx extension). Legacy .doc files aren't supported.");
        }

        var sb = new StringBuilder();
        try
        {
            buffer.Position = 0;
            using var doc = WordprocessingDocument.Open(buffer, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return string.Empty;

            // Every paragraph in document order (table cells included), one blank-line-separated block
            // each so the chunker's paragraph splitting works. Deleted (tracked-change) text is excluded.
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();
                foreach (var node in paragraph.Descendants())
                {
                    switch (node)
                    {
                        case Text t: sb.Append(t.Text); break;
                        case TabChar: sb.Append(' '); break;
                        case Break: sb.Append('\n'); break;
                    }
                }
                sb.Append("\n\n");
                if (sb.Length > _maxChars * 2) break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidDocumentException("Couldn't read this DOCX. It may be corrupted or password-protected.");
        }
        return sb.ToString();
    }

    private static string ExtractText(ReadOnlySpan<byte> bytes)
    {
        // A NUL byte means binary data with a .txt name (UTF-16 legitimately contains them, but it has a BOM).
        var isUtf16Le = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE;
        var isUtf16Be = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
        if (!isUtf16Le && !isUtf16Be && bytes.IndexOf((byte)0) >= 0)
        {
            throw new InvalidDocumentException("This file doesn't look like plain text.");
        }

        if (isUtf16Le) return Encoding.Unicode.GetString(bytes);
        if (isUtf16Be) return Encoding.BigEndianUnicode.GetString(bytes);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8 — most likely a legacy Windows/Latin-1 text file.
            return Encoding.Latin1.GetString(bytes);
        }
    }
}

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
