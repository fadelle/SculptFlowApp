using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Dashboard;

/// <summary>
/// Staff-facing edit page for the three lead classification fields (Lead Status, Qualification
/// Status, Lead Source) — the ones Campaign audiences (ICampaignAudienceService) filter on. Posts
/// through the same ILeadService.UpdateStatusAsync the existing POST /api/leads/{id}/status endpoint
/// already uses (UpdateLeadStatusRequest just gained an optional Source field for this — see its
/// remarks), so there's no second write path and Campaign counts see the change immediately (they
/// read live from the database, no caching).
/// </summary>
public class LeadDetailModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly ILeadService _leads;

    public LeadDetailModel(ICurrentClinicContext clinicContext, ILeadService leads)
    {
        _clinicContext = clinicContext;
        _leads = leads;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClinicConfigured { get; private set; }
    public LeadResponse? Lead { get; private set; }

    public IReadOnlyList<string> StatusOptions { get; } = LeadStatus.All.OrderBy(s => s).ToList();
    public IReadOnlyList<string> QualificationOptions { get; } = LeadQualificationStatus.All.OrderBy(s => s).ToList();

    /// <summary>Distinct sources already on file for this clinic, unioned with a small starter set
    /// (the examples the product spec gave) so a brand-new clinic with no lead data yet still has
    /// sensible options — not a fixed enum, since Lead.Source has none (see UpdateLeadStatusRequest).</summary>
    public IReadOnlyList<string> SourceOptions { get; private set; } = Array.Empty<string>();

    private static readonly string[] SuggestedSources = { "facebook", "instagram", "google", "website", "referral", "whatsapp" };

    [BindProperty]
    public string Status { get; set; } = string.Empty;

    [BindProperty]
    public string QualificationStatus { get; set; } = string.Empty;

    [BindProperty]
    public string SourceChoice { get; set; } = string.Empty;

    /// <summary>Used only when SourceChoice == "other" — lets staff enter a source not already in
    /// SourceOptions instead of being blocked by a fixed list.</summary>
    [BindProperty]
    public string? CustomSource { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (ClinicConfigured && Lead is null) return NotFound();
        if (Lead is not null)
        {
            Status = Lead.Status;
            QualificationStatus = Lead.QualificationStatus;
            SourceChoice = !string.IsNullOrWhiteSpace(Lead.Source) && SourceOptions.Contains(Lead.Source, StringComparer.OrdinalIgnoreCase)
                ? Lead.Source : "other";
            CustomSource = SourceChoice == "other" ? Lead.Source : null;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        var resolvedSource = SourceChoice == "other" ? CustomSource?.Trim() : SourceChoice;

        try
        {
            var lead = await _leads.UpdateStatusAsync(clinic.Id, Id,
                new UpdateLeadStatusRequest(Status, QualificationStatus, resolvedSource), ct);
            if (lead is null) return NotFound();

            StatusMessage = "Saved.";
            return RedirectToPage(new { Id });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;

        Lead = await _leads.GetByIdAsync(clinic.Id, Id, ct);
        if (Lead is null) return;

        var distinctSources = await _leads.GetDistinctSourcesAsync(clinic.Id, ct);
        var options = new List<string>(distinctSources);
        foreach (var s in SuggestedSources)
        {
            if (!options.Contains(s, StringComparer.OrdinalIgnoreCase)) options.Add(s);
        }
        if (!string.IsNullOrWhiteSpace(Lead.Source) && !options.Contains(Lead.Source, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(Lead.Source);
        }
        options.Sort(StringComparer.OrdinalIgnoreCase);
        SourceOptions = options;
    }
}
