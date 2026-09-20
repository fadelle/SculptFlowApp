using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Knowledge.WebScraping;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase;

/// <summary>
/// Knowledge Base list — ONE list of records the AI assistant can learn from: each manual entry, each uploaded
/// file, and each imported WEBSITE as a single record (its individual pages are not listed here — clicking the
/// website record opens its own page with every page that was scraped). The clinic always comes from the
/// logged-in user (ICurrentClinicContext).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteSourceService _websites;

    public IndexModel(ICurrentClinicContext clinicContext, IKnowledgeService knowledge, IWebsiteSourceService websites)
    {
        _clinicContext = clinicContext;
        _knowledge = knowledge;
        _websites = websites;
    }

    /// <summary>One line of the list: either a document (manual/upload) or a website source.</summary>
    public sealed record Row(DateTimeOffset SortDate, KnowledgeDocumentResponse? Document, WebsiteSourceResponse? Website);

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();
    public bool AnyWebsiteInProgress => Rows.Any(r => r.Website is { InProgress: true });

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return;

        ClinicConfigured = true;

        // Pages imported from a website are represented by their website's record, not listed one by one.
        var documents = (await _knowledge.ListAsync(clinic.Id, ct)).Where(d => d.SourceType != KnowledgeSourceType.Website);
        var websites = await _websites.ListAsync(clinic.Id, ct);

        Rows = documents.Select(d => new Row(d.UpdatedAt, d, null))
            .Concat(websites.Select(w => new Row(w.LastScrapedAt ?? w.CreatedAt, null, w)))
            .OrderByDescending(r => r.SortDate)
            .ToList();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        var doc = await _knowledge.SetActiveAsync(clinic.Id, id, active, ct);
        StatusMessage = doc is null ? null : (active ? "Activated — the AI can use it again." : "Deactivated — the AI will no longer use it.");
        if (doc is null) ErrorMessage = "Entry not found.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        if (await _knowledge.DeleteAsync(clinic.Id, id, ct)) StatusMessage = "Deleted.";
        else ErrorMessage = "Entry not found.";
        return RedirectToPage();
    }

    /// <summary>Activate/deactivate a whole website: all of its imported pages follow.</summary>
    public async Task<IActionResult> OnPostToggleWebsiteAsync(Guid id, bool active, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        if (await _websites.SetActiveAsync(clinic.Id, id, active, ct) is null) ErrorMessage = "Website not found.";
        else StatusMessage = active
            ? "Website activated — its pages can be used by the AI again."
            : "Website deactivated — its pages are hidden from the AI.";
        return RedirectToPage();
    }

    /// <summary>Deletes the website and the Knowledge Base pages imported from it (nothing else).</summary>
    public async Task<IActionResult> OnPostDeleteWebsiteAsync(Guid id, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            if (await _websites.DeleteAsync(clinic.Id, id, ct)) StatusMessage = "Website and its imported pages deleted.";
            else ErrorMessage = "Website not found.";
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage();
    }
}
