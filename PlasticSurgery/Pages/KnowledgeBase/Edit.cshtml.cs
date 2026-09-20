using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Knowledge.WebScraping;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase;

/// <summary>Add / edit one Knowledge Base entry. Adding offers two ways in — type it (Manual Entry) or
/// upload ONE PDF/DOCX/TXT (Upload Document) — both ending in the same IKnowledgeService pipeline. For an
/// uploaded document, editing changes only title/category/active (the extracted text is shown read-only).
/// Saving re-chunks and re-embeds the text; the clinic always comes from the logged-in user.</summary>
public class EditModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteSourceService _websites;

    public EditModel(ICurrentClinicContext clinicContext, IKnowledgeService knowledge, IWebsiteSourceService websites)
    {
        _clinicContext = clinicContext;
        _knowledge = knowledge;
        _websites = websites;
    }

    /// <summary>Route value — null when adding a new entry.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    public bool ClinicConfigured { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Category { get; set; } = KnowledgeCategory.General;

    [BindProperty]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public bool IsActive { get; set; }

    /// <summary>"upload" preselects the Upload Document tab when adding (?mode=upload).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Mode { get; set; }

    /// <summary>The single file for the Upload Document form.</summary>
    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public bool ShowUploadTab => string.Equals(Mode, "upload", StringComparison.OrdinalIgnoreCase);
    public bool ShowWebsiteTab => string.Equals(Mode, "website", StringComparison.OrdinalIgnoreCase);

    /// <summary>Website tab: the address to import (one website per submission).</summary>
    [BindProperty]
    public string? WebsiteUrl { get; set; }

    /// <summary>"crawl_site" (follow links on the same website) or "single_page".</summary>
    [BindProperty]
    public string WebsiteMode { get; set; } = WebsiteCrawlMode.CrawlSite;

    /// <summary>Set when editing a document that was imported from a website page.</summary>
    public bool IsWebsite { get; private set; }
    public string? SourceUrl { get; private set; }

    /// <summary>Set when editing an existing document that came from an upload.</summary>
    public bool IsUpload { get; private set; }
    public string? OriginalFileName { get; private set; }
    public long? FileSizeBytes { get; private set; }

    public long MaxUploadBytes => _knowledge.MaxUploadBytes;
    public string AcceptedTypes => string.Join(", ", DocumentTextExtractor.SupportedExtensions);

    public string? ErrorMessage { get; set; }

    /// <summary>The standard categories, plus the entry's own if it was saved with a custom one.</summary>
    public IReadOnlyList<(string Value, string Label)> CategoryOptions =>
        KnowledgeCategory.All.Any(c => c.Value == Category) || string.IsNullOrWhiteSpace(Category)
            ? KnowledgeCategory.All
            : KnowledgeCategory.All.Append((Category, KnowledgeCategory.Label(Category))).ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return Page();
        ClinicConfigured = true;

        if (Id is null)
        {
            IsActive = true;
            return Page();
        }

        var doc = await _knowledge.GetByIdAsync(clinic.Id, Id.Value, ct);
        if (doc is null) return NotFound();

        Title = doc.Title;
        Category = doc.Category;
        Body = doc.Content;
        IsActive = doc.IsActive;
        IsUpload = doc.SourceType == KnowledgeSourceType.Upload;
        IsWebsite = doc.SourceType == KnowledgeSourceType.Website;
        SourceUrl = doc.SourceUrl;
        OriginalFileName = doc.OriginalFileName;
        FileSizeBytes = doc.FileSizeBytes;
        return Page();
    }

    /// <summary>Website tab: register ONE website and queue its crawl. Returns immediately — the crawl runs in the
    /// background and its progress is shown on the website's page.</summary>
    public async Task<IActionResult> OnPostWebsiteAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        ClinicConfigured = true;
        Mode = "website"; // re-show the website tab if we redisplay the page with an error

        if (Id is not null) return BadRequest();

        try
        {
            var source = await _websites.CreateAsync(clinic.Id, new CreateWebsiteSourceRequest(WebsiteUrl, WebsiteMode, Category, IsActive), ct);
            TempData["StatusMessage"] = "Website added — the crawl has started. This page updates as it runs.";
            return Redirect($"/KnowledgeBase/Websites/{source.Id}");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        return Page();
    }

    /// <summary>Upload Document tab: one file → extract text → same pipeline as a manual entry.</summary>
    public async Task<IActionResult> OnPostUploadAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        ClinicConfigured = true;
        Mode = "upload"; // re-show the upload tab if we redisplay the page with an error

        // Uploads are only for creating a new document (to change one's text, upload it again).
        if (Id is not null) return BadRequest();

        try
        {
            if (UploadFile is null || UploadFile.Length == 0)
            {
                throw new InvalidDocumentException(UploadFile is null ? "Choose a file to upload." : "The file is empty.");
            }

            await using var stream = UploadFile.OpenReadStream();
            var doc = await _knowledge.CreateFromUploadAsync(
                clinic.Id, new UploadKnowledgeRequest(Title, Category, IsActive),
                UploadFile.FileName, stream, UploadFile.Length, ct);

            TempData["StatusMessage"] = $"Uploaded \"{doc.Title}\" — {doc.ChunkCount} searchable piece{(doc.ChunkCount == 1 ? "" : "s")} created.";
            return RedirectToPage("/KnowledgeBase/Index");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"Couldn't index this document for AI search: {ex.Message}";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        ClinicConfigured = true;

        if (Id is not null)
        {
            // Know whether this is an uploaded document so a redisplay after an error renders it correctly.
            var existing = await _knowledge.GetByIdAsync(clinic.Id, Id.Value, ct);
            if (existing is null) return NotFound();
            IsUpload = existing.SourceType == KnowledgeSourceType.Upload;
            IsWebsite = existing.SourceType == KnowledgeSourceType.Website;
            SourceUrl = existing.SourceUrl;
            OriginalFileName = existing.OriginalFileName;
            FileSizeBytes = existing.FileSizeBytes;
            if (IsUpload || IsWebsite) Body = existing.Content;
        }

        try
        {
            var request = new SaveKnowledgeRequest(Title, Category, Body, IsActive);
            if (Id is null)
            {
                await _knowledge.CreateAsync(clinic.Id, request, ct);
            }
            else if (await _knowledge.UpdateAsync(clinic.Id, Id.Value, request, ct) is null)
            {
                return NotFound();
            }

            TempData["StatusMessage"] = "Saved.";
            return RedirectToPage("/KnowledgeBase/Index");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"Couldn't index this entry for AI search: {ex.Message}";
        }
        return Page();
    }
}
