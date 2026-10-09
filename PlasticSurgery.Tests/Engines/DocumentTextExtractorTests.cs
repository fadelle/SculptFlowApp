using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PlasticSurgery.Business.Engines.Knowledge;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;

namespace PlasticSurgery.Tests.Engines;

public class DocumentTextExtractorTests
{
    private readonly Mock<IConfigManager> _config = new();

    public DocumentTextExtractorTests() => _config.SetupGet(c => c.KnowledgeMaxExtractedChars).Returns(5000);

    private DocumentTextExtractor Sut() => new(_config.Object);

    private Task<string> Extract(byte[] bytes, string name) => Sut().ExtractAsync(new MemoryStream(bytes), name);

    private static byte[] Docx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs.Select(p => new Paragraph(new Run(new Text(p) { Space = SpaceProcessingModeValues.Preserve })))));
        }
        return ms.ToArray();
    }

    private static byte[] Pdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(595, 842);
            page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        }
        return builder.Build();
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("A.DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.exe", "application/octet-stream")]
    [InlineData("noext", "application/octet-stream")]
    public void Mime_types(string name, string mime) => Assert.Equal(mime, DocumentTextExtractor.MimeTypeFor(name));

    [Fact]
    public async Task Plain_text_is_normalised()
    {
        Assert.Equal("Hello world\n\nSecond", await Extract(Encoding.UTF8.GetBytes("Hello   world\r\n\r\n\r\n\r\nSecond  "), "a.txt"));
    }

    [Fact]
    public async Task Text_encodings_utf8_bom_utf16_and_latin1()
    {
        Assert.Equal("café", await Extract(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("café")).ToArray(), "a.txt"));
        Assert.Equal("hi there", await Extract(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hi there")).ToArray(), "a.txt"));
        Assert.Equal("hi there", await Extract(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("hi there")).ToArray(), "a.txt"));
        Assert.Equal("café", await Extract(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, "a.txt")); // invalid UTF-8 → Latin-1
    }

    [Fact]
    public async Task Binary_content_with_a_txt_name_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(new byte[] { 1, 2, 0, 3 }, "a.txt"));
        Assert.Contains("plain text", ex.Message);
    }

    [Theory]
    [InlineData("a.doc")]
    [InlineData("a")]
    [InlineData("a.png")]
    public async Task Unsupported_types_are_rejected(string name) =>
        await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract([1], name));

    [Fact]
    public async Task Empty_or_whitespace_only_files_are_rejected()
    {
        await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract([], "a.txt"));
        var ex = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Encoding.UTF8.GetBytes("  \n\t "), "a.txt"));
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task Too_much_text_is_rejected_with_the_limit_in_the_message()
    {
        var ex = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Encoding.UTF8.GetBytes(new string('x', 5001)), "a.txt"));
        Assert.Contains("too much text", ex.Message);
        Assert.Equal(5000, (await Extract(Encoding.UTF8.GetBytes(new string('x', 5000)), "a.txt")).Length);
    }

    [Fact]
    public async Task Docx_paragraphs_become_blank_line_separated_blocks()
    {
        Assert.Equal("First paragraph\n\nSecond paragraph", await Extract(Docx("First paragraph", "Second paragraph"), "a.docx"));
    }

    [Fact]
    public async Task Docx_rejects_fake_corrupt_and_textless_files()
    {
        var fake = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Encoding.UTF8.GetBytes("not a zip"), "a.docx"));
        Assert.Contains("valid DOCX", fake.Message);
        var corrupt = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract([0x50, 0x4B, 0x03, 0x04, 1, 2, 3], "a.docx"));
        Assert.Contains("Couldn't read", corrupt.Message);
        var empty = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Docx(""), "a.docx"));
        Assert.Contains("readable text", empty.Message);
    }

    [Fact]
    public async Task Pdf_text_is_extracted_page_by_page()
    {
        var text = await Extract(Pdf("Rhinoplasty pricing", "Recovery guide"), "a.pdf");
        Assert.Contains("Rhinoplasty pricing", text);
        Assert.Contains("Recovery guide", text);
    }

    [Fact]
    public async Task Pdf_rejects_fake_corrupt_and_textless_files()
    {
        var fake = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Encoding.UTF8.GetBytes("plain"), "a.pdf"));
        Assert.Contains("valid PDF", fake.Message);
        var corrupt = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Encoding.ASCII.GetBytes("%PDF-1.4 garbage"), "a.pdf"));
        Assert.Contains("Couldn't read", corrupt.Message);
        var blank = await Assert.ThrowsAsync<InvalidDocumentException>(() => Extract(Pdf(" "), "a.pdf"));
        Assert.Contains("readable text", blank.Message);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sut().ExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes("x")), "a.txt", cts.Token));
    }
}
