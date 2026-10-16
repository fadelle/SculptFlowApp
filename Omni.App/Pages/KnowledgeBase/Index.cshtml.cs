using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Knowledge;

namespace Omni.App.Pages.KnowledgeBase;

/// <summary>
/// Knowledge Base list — ONE list of records the AI assistant can learn from: each manual entry, each uploaded
/// file, and each imported WEBSITE as a single record (its individual pages are not listed here — clicking the
/// website record opens its own page with every page that was scraped). The client always comes from the
/// logged-in user (ICurrentClientContext).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteSourceService _websites;

    public IndexModel(ICurrentClientContext clientContext, IKnowledgeService knowledge, IWebsiteSourceService websites)
    {
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

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return;

        ClientConfigured = true;

        // Pages imported from a website are represented by their website's record, not listed one by one.
        var documents = (await _knowledge.ListAsync(client.Id, ct)).Where(d => d.SourceType != KnowledgeSourceType.Website);
        var websites = await _websites.ListAsync(client.Id, ct);

        Rows = documents.Select(d => new KnowledgeListRow(d.UpdatedAt, d, null))
            .Concat(websites.Select(w => new KnowledgeListRow(w.LastScrapedAt ?? w.CreatedAt, null, w)))
            .OrderByDescending(r => r.SortDate)
            .ToList();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var doc = await _knowledge.SetActiveAsync(client.Id, id, active, ct);
        StatusMessage = doc is null ? null : (active ? "Activated — the AI can use it again." : "Deactivated — the AI will no longer use it.");
        if (doc is null) ErrorMessage = "Entry not found.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        if (await _knowledge.DeleteAsync(client.Id, id, ct)) StatusMessage = "Deleted.";
        else ErrorMessage = "Entry not found.";
        return RedirectToPage();
    }

    /// <summary>Activate/deactivate a whole website: all of its imported pages follow.</summary>
    public async Task<IActionResult> OnPostToggleWebsiteAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        if (await _websites.SetActiveAsync(client.Id, id, active, ct) is null) ErrorMessage = "Website not found.";
        else StatusMessage = active
            ? "Website activated — its pages can be used by the AI again."
            : "Website deactivated — its pages are hidden from the AI.";
        return RedirectToPage();
    }

    /// <summary>Deletes the website and the Knowledge Base pages imported from it (nothing else).</summary>
    public async Task<IActionResult> OnPostDeleteWebsiteAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            if (await _websites.DeleteAsync(client.Id, id, ct)) StatusMessage = "Website and its imported pages deleted.";
            else ErrorMessage = "Website not found.";
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage();
    }
}
