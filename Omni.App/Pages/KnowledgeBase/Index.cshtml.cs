using Omni.App.Controllers.Filters;
using Omni.App.Common.Statics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Requests.Knowledge;

namespace Omni.App.Pages.KnowledgeBase;

/// <summary>
/// Knowledge Base list — ONE list of records the AI assistant can learn from: each manual entry, each uploaded
/// file, and each imported WEBSITE as a single record (its individual pages are not listed here — clicking the
/// website record opens its own page with every page that was scraped). The client always comes from the
/// logged-in user (ICurrentClientContext).
/// Adding and editing happen in a popup on this page. Adding offers three ways in: type it (Save handler), upload ONE
/// PDF/DOCX/TXT (Upload) or import a website (Website); ?add=manual|upload|website or ?edit={id} opens the popup from a
/// link. For an uploaded or website document, editing changes only title/category/active (its text is shown read-only).
/// Saving re-chunks and re-embeds the text.
/// </summary>
[RequirePlanFeature(EntitlementKeys.AiAssistant)]
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteSourceService _websites;

    private const string ActivatedKey = "knowledge.entry.activated";
    private const string DeactivatedKey = "knowledge.entry.deactivated";
    private const string EntryNotFoundKey = "knowledge.entry.notFound";
    private const string DeletedKey = "knowledge.entry.deleted";
    private const string WebsiteNotFoundKey = "knowledge.website.notFound";
    private const string WebsiteActivatedKey = "knowledge.website.activated";
    private const string WebsiteDeactivatedKey = "knowledge.website.deactivated";
    private const string WebsiteDeletedKey = "knowledge.website.deleted";
    private const string WebsiteAddedKey = "knowledge.website.added";
    private const string UploadedPluralKey = "knowledge.upload.done";
    private const string UploadIndexFailedKey = "knowledge.upload.indexFailed";
    private const string SavedKey = "knowledge.entry.saved";
    private const string EntryIndexFailedKey = "knowledge.entry.indexFailed";

    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IKnowledgeService knowledge, IWebsiteSourceService websites, IStringLocalizer text)
    {
        _text = text;
        _clientContext = clientContext;
        _knowledge = knowledge;
        _websites = websites;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<KnowledgeListRow> Rows { get; private set; } = Array.Empty<KnowledgeListRow>();
    public bool AnyWebsiteInProgress => Rows.Any(r => r.Website is { InProgress: true });

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    // ---- the add / edit popup

    /// <summary>?add=manual|upload|website opens the popup on that way in.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Add { get; set; }

    /// <summary>?edit={id} opens the popup on that entry; also the entry the Save handler changes (null = new).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    /// <summary>Where closing the popup and a successful save go (a website's page opens "View text" with it). Only local
    /// Knowledge Base addresses are honored.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty] public string Title { get; set; } = string.Empty;
    [BindProperty] public string Category { get; set; } = KnowledgeCategory.General;
    [BindProperty] public string Body { get; set; } = string.Empty;
    /// <summary>Unchecked boxes post nothing, so this defaults to false; a new entry starts on (OnGet).</summary>
    [BindProperty] public bool IsActive { get; set; }
    [BindProperty] public IFormFile? UploadFile { get; set; }
    [BindProperty] public string? WebsiteUrl { get; set; }

    /// <summary>"crawl_site" (follow links on the same website) or "single_page".</summary>
    [BindProperty] public string WebsiteMode { get; set; } = WebsiteCrawlMode.CrawlSite;

    /// <summary>The popup is open on load; FormMode is the way in it shows (manual, upload, website).</summary>
    public bool FormOpen { get; private set; }
    public string FormMode { get; private set; } = "manual";
    public string? FormError { get; private set; }

    /// <summary>Editing a document imported from a website page or uploaded: its text is read-only.</summary>
    public bool IsWebsite { get; private set; }
    public bool IsUpload { get; private set; }
    public string? SourceUrl { get; private set; }
    public string? OriginalFileName { get; private set; }
    public long? FileSizeBytes { get; private set; }

    public long MaxUploadBytes => _knowledge.MaxUploadBytes;

    public string BackUrl =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && ReturnUrl.StartsWith("/KnowledgeBase", StringComparison.OrdinalIgnoreCase)
            ? ReturnUrl
            : "/KnowledgeBase";

    /// <summary>The standard categories, plus the entry's own if it was saved with a custom one.</summary>
    public IReadOnlyList<string> CategoryOptions =>
        KnowledgeCategory.All.Contains(Category) || string.IsNullOrWhiteSpace(Category)
            ? KnowledgeCategory.All
            : KnowledgeCategory.All.Append(Category).ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return Page();

        IsActive = true;
        if (Edit is { } id)
        {
            var doc = await _knowledge.GetByIdAsync(client.Id, id, ct);
            if (doc is null) return NotFound();
            Title = doc.Title;
            Category = doc.Category;
            Body = doc.Content;
            IsActive = doc.IsActive;
            ShowSource(doc.SourceType, doc.SourceUrl, doc.OriginalFileName, doc.FileSizeBytes);
            FormOpen = true;
        }
        else if (Add is "manual" or "upload" or "website")
        {
            FormOpen = true;
            FormMode = Add;
        }

        await LoadListAsync(client.Id, ct);
        return Page();
    }

    /// <summary>Written entry: create one, or save an entry's title/category/active (and text, for a written one).</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        if (Edit is not null)
        {
            // Know whether this is an uploaded/website document so a redisplay after an error renders it correctly.
            var existing = await _knowledge.GetByIdAsync(client.Id, Edit.Value, ct);
            if (existing is null) return NotFound();
            ShowSource(existing.SourceType, existing.SourceUrl, existing.OriginalFileName, existing.FileSizeBytes);
            if (IsUpload || IsWebsite) Body = existing.Content;
        }

        try
        {
            var request = new SaveKnowledgeRequest(Title, Category, Body, IsActive);
            if (Edit is null)
            {
                await _knowledge.CreateAsync(client.Id, request, ct);
            }
            else if (await _knowledge.UpdateAsync(client.Id, Edit.Value, request, ct) is null)
            {
                return NotFound();
            }

            StatusMessage = _text[SavedKey].Value;
            return LocalRedirect(BackUrl); // back to where the user came from (the main list, or a website's page)
        }
        catch (ArgumentException ex)
        {
            FormError = _text.Localize(ex);
        }
        catch (InvalidOperationException ex)
        {
            FormError = _text[EntryIndexFailedKey, _text.Localize(ex)].Value;
        }
        return await RedisplayAsync(client.Id, "manual", ct);
    }

    /// <summary>Upload: one file → extract text → same pipeline as a written entry.</summary>
    public async Task<IActionResult> OnPostUploadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        Edit = null; // uploads only create (to change a document's text, upload it again)

        try
        {
            if (UploadFile is null || UploadFile.Length == 0)
            {
                throw UploadFile is null
                    ? new InvalidDocumentException("knowledge.upload.errors.noFile")
                    : new InvalidDocumentException("knowledge.upload.errors.emptyFile");
            }

            await using var stream = UploadFile.OpenReadStream();
            var doc = await _knowledge.CreateFromUploadAsync(
                client.Id, new UploadKnowledgeRequest(Title, Category, IsActive),
                UploadFile.FileName, stream, UploadFile.Length, ct);

            StatusMessage = _text.Plural(UploadedPluralKey, doc.ChunkCount, doc.Title, doc.ChunkCount).Value;
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            FormError = _text.Localize(ex);
        }
        catch (InvalidOperationException ex)
        {
            FormError = _text[UploadIndexFailedKey, _text.Localize(ex)].Value;
        }
        return await RedisplayAsync(client.Id, "upload", ct);
    }

    /// <summary>Website: register ONE website and queue its crawl. Returns immediately — the crawl runs in the
    /// background and its progress is shown on the website's page.</summary>
    public async Task<IActionResult> OnPostWebsiteAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        Edit = null;

        try
        {
            var source = await _websites.CreateAsync(client.Id, new CreateWebsiteSourceRequest(WebsiteUrl, WebsiteMode, Category, IsActive), ct);
            StatusMessage = _text[WebsiteAddedKey].Value;
            return Redirect($"/KnowledgeBase/Websites/{source.Id}");
        }
        catch (ArgumentException ex)
        {
            FormError = _text.Localize(ex);
        }
        return await RedisplayAsync(client.Id, "website", ct);
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var doc = await _knowledge.SetActiveAsync(client.Id, id, active, ct);
        StatusMessage = doc is null ? null : _text[active ? ActivatedKey : DeactivatedKey].Value;
        if (doc is null) ErrorMessage = _text[EntryNotFoundKey].Value;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        if (await _knowledge.DeleteAsync(client.Id, id, ct)) StatusMessage = _text[DeletedKey].Value;
        else ErrorMessage = _text[EntryNotFoundKey].Value;
        return RedirectToPage();
    }

    /// <summary>Activate/deactivate a whole website: all of its imported pages follow.</summary>
    public async Task<IActionResult> OnPostToggleWebsiteAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        if (await _websites.SetActiveAsync(client.Id, id, active, ct) is null) ErrorMessage = _text[WebsiteNotFoundKey].Value;
        else StatusMessage = _text[active ? WebsiteActivatedKey : WebsiteDeactivatedKey].Value;
        return RedirectToPage();
    }

    /// <summary>Deletes the website and the Knowledge Base pages imported from it (nothing else).</summary>
    public async Task<IActionResult> OnPostDeleteWebsiteAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            if (await _websites.DeleteAsync(client.Id, id, ct)) StatusMessage = _text[WebsiteDeletedKey].Value;
            else ErrorMessage = _text[WebsiteNotFoundKey].Value;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }
        return RedirectToPage();
    }

    /// <summary>A failed add/save shows the list again with the popup open on what was posted.</summary>
    private async Task<IActionResult> RedisplayAsync(Guid clientId, string mode, CancellationToken ct)
    {
        FormOpen = true;
        FormMode = mode;
        await LoadListAsync(clientId, ct);
        return Page();
    }

    private void ShowSource(string sourceType, string? sourceUrl, string? originalFileName, long? fileSizeBytes)
    {
        IsUpload = sourceType == KnowledgeSourceType.Upload;
        IsWebsite = sourceType == KnowledgeSourceType.Website;
        SourceUrl = sourceUrl;
        OriginalFileName = originalFileName;
        FileSizeBytes = fileSizeBytes;
    }

    private async Task LoadListAsync(Guid clientId, CancellationToken ct)
    {
        ClientConfigured = true;

        // Pages imported from a website are represented by their website's record, not listed one by one.
        var documents = (await _knowledge.ListAsync(clientId, ct)).Where(d => d.SourceType != KnowledgeSourceType.Website);
        var websites = await _websites.ListAsync(clientId, ct);

        Rows = documents.Select(d => new KnowledgeListRow(d.UpdatedAt, d, null))
            .Concat(websites.Select(w => new KnowledgeListRow(w.LastScrapedAt ?? w.CreatedAt, null, w)))
            .OrderByDescending(r => r.SortDate)
            .ToList();
    }
}
