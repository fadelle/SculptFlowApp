using Omni.App.Controllers.Filters;
using Omni.App.Common.Statics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Pages.KnowledgeBase.Websites;

/// <summary>One website source: crawl status and counts (live while it runs), page list with failures, and the
/// actions Re-scrape / Activate-Deactivate / Delete. The client always comes from the logged-in user.</summary>
[RequirePlanFeature(EntitlementKeys.AiAssistant)]
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

    /// <summary>Pages per list page (QA t4-ai-13: a 100-page crawl was one 50,000 px list on a phone).</summary>
    public const int PageSize = 50;
    /// <summary>The most pages one list reads (the service's cap); a crawl is limited by WebScraping:MaxPages.</summary>
    private const int ListCap = 500;

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public bool ClientConfigured { get; private set; }
    public WebsiteSourceDetailResponse? Detail { get; private set; }
    public IReadOnlyList<WebsitePageResponse> Pages { get; private set; } = Array.Empty<WebsitePageResponse>();
    /// <summary>Pages in the current list (all list pages).</summary>
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
    /// <summary>In the "All" view, the pages already in the Knowledge Base are folded into one line with their count (and a link
    /// to them), so the pages with a problem come first (QA t4-ai-13).</summary>
    public int FoldedInKnowledge { get; private set; }

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
        IReadOnlyList<WebsitePageResponse> list;
        if (Status == "in_kb")
        {
            list = (await _websites.ListPagesAsync(client.Id, Id, WebsitePageStatus.Indexed, 0, ListCap, ct))
                .Concat(await _websites.ListPagesAsync(client.Id, Id, WebsitePageStatus.Unchanged, 0, ListCap, ct)).ToList();
        }
        else if (!string.IsNullOrEmpty(Status))
        {
            list = await _websites.ListPagesAsync(client.Id, Id, Status, 0, ListCap, ct);
        }
        else
        {
            var every = await _websites.ListPagesAsync(client.Id, Id, null, 0, ListCap, ct);
            FoldedInKnowledge = every.Count(p => InKnowledge(p.Status));
            list = every.Where(p => !InKnowledge(p.Status)).OrderBy(p => ProblemRank(p.Status)).ThenBy(p => p.Url, StringComparer.Ordinal).ToList();
        }
        TotalCount = list.Count;
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Pages = list.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        return Page();
    }

    private static bool InKnowledge(string status) => status is WebsitePageStatus.Indexed or WebsitePageStatus.Unchanged;

    /// <summary>Failed first, then skipped, duplicate, removed, then the ones still being read.</summary>
    internal static int ProblemRank(string status) => status switch
    {
        WebsitePageStatus.Failed => 0,
        WebsitePageStatus.Skipped => 1,
        WebsitePageStatus.Duplicate => 2,
        WebsitePageStatus.Removed => 3,
        _ => 4
    };

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
