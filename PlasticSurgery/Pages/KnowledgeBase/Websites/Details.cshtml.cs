using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Knowledge.WebScraping;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase.Websites;

/// <summary>One website source: crawl status and counts (live while it runs), page list with failures, and the
/// actions Re-scrape / Activate-Deactivate / Delete. The clinic always comes from the logged-in user.</summary>
public class DetailsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IWebsiteSourceService _websites;

    public DetailsModel(ICurrentClinicContext clinicContext, IWebsiteSourceService websites)
    {
        _clinicContext = clinicContext;
        _websites = websites;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    /// <summary>Page-list filter (?status=failed ...); empty = all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public bool ClinicConfigured { get; private set; }
    public WebsiteSourceDetailResponse? Detail { get; private set; }
    public IReadOnlyList<WebsitePageResponse> Pages { get; private set; } = Array.Empty<WebsitePageResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return Page();
        ClinicConfigured = true;

        Detail = await _websites.GetAsync(clinic.Id, Id, ct);
        if (Detail is null) return NotFound();
        Pages = await _websites.ListPagesAsync(clinic.Id, Id, Status, 0, 300, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRescrapeAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        try
        {
            if (await _websites.RescrapeAsync(clinic.Id, Id, ct) is null) return NotFound();
            StatusMessage = "Re-scrape started. This page updates as it runs.";
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostToggleAsync(bool active, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        if (await _websites.SetActiveAsync(clinic.Id, Id, active, ct) is null) return NotFound();
        StatusMessage = active
            ? "Website activated — its pages can be used by the AI again."
            : "Website deactivated — its pages are hidden from the AI.";
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        try
        {
            if (!await _websites.DeleteAsync(clinic.Id, Id, ct)) return NotFound();
            TempData["StatusMessage"] = "Website and its imported pages deleted.";
            return Redirect("/KnowledgeBase");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToPage(new { id = Id });
        }
    }
}
