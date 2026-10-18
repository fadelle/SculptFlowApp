using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Pages.KnowledgeBase.Websites;

/// <summary>One website source: crawl status and counts (live while it runs), page list with failures, and the
/// actions Re-scrape / Activate-Deactivate / Delete. The client always comes from the logged-in user.</summary>
public class DetailsModel : PageModel
{
    private const string RescrapeStartedKey = "website.rescrapeStarted";
    private const string ActivatedKey = "knowledge.website.activated";
    private const string DeactivatedKey = "knowledge.website.deactivated";
    private const string DeletedKey = "knowledge.website.deleted";

    private readonly ICurrentClientContext _clientContext;
    private readonly IWebsiteSourceService _websites;
    private readonly IStringLocalizer _text;

    public DetailsModel(ICurrentClientContext clientContext, IWebsiteSourceService websites, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _websites = websites;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    /// <summary>Page-list filter (?status=failed ...); empty = all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public bool ClientConfigured { get; private set; }
    public WebsiteSourceDetailResponse? Detail { get; private set; }
    public IReadOnlyList<WebsitePageResponse> Pages { get; private set; } = Array.Empty<WebsitePageResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return Page();
        ClientConfigured = true;

        Detail = await _websites.GetAsync(client.Id, Id, ct);
        if (Detail is null) return NotFound();
        // "In Knowledge Base" = indexed + unchanged pages (both are what the AI can use).
        Pages = Status == "in_kb"
            ? (await _websites.ListPagesAsync(client.Id, Id, Omni.App.Common.Enums.WebsitePageStatus.Indexed, 0, 300, ct))
                .Concat(await _websites.ListPagesAsync(client.Id, Id, Omni.App.Common.Enums.WebsitePageStatus.Unchanged, 0, 300, ct)).ToList()
            : await _websites.ListPagesAsync(client.Id, Id, Status, 0, 300, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRescrapeAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage("/KnowledgeBase/Index");
        try
        {
            if (await _websites.RescrapeAsync(client.Id, Id, ct) is null) return NotFound();
            StatusMessage = _text[RescrapeStartedKey].Value;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostToggleAsync(bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage("/KnowledgeBase/Index");
        if (await _websites.SetActiveAsync(client.Id, Id, active, ct) is null) return NotFound();
        StatusMessage = _text[active ? ActivatedKey : DeactivatedKey].Value;
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage("/KnowledgeBase/Index");
        try
        {
            if (!await _websites.DeleteAsync(client.Id, Id, ct)) return NotFound();
            TempData["StatusMessage"] = _text[DeletedKey].Value;
            return Redirect("/KnowledgeBase");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            return RedirectToPage(new { id = Id });
        }
    }
}
