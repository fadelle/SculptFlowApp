using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Leads;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Leads;
using Omni.App.Entities.Responses.Leads;

namespace Omni.App.Pages.Dashboard;

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
    private readonly ICurrentClientContext _clientContext;
    private readonly ILeadService _leads;
    private readonly IStringLocalizer _text;

    public LeadDetailModel(ICurrentClientContext clientContext, ILeadService leads, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _leads = leads;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClientConfigured { get; private set; }
    public LeadResponse? Lead { get; private set; }

    /// <summary>The client's pipeline stages in order, labelled (status badge and dropdown).</summary>
    public IReadOnlyList<PipelineStageOption> Stages { get; private set; } = Array.Empty<PipelineStageOption>();
    public IReadOnlyList<string> QualificationOptions { get; } = LeadQualificationStatus.All.OrderBy(s => s).ToList();

    /// <summary>Distinct sources already on file for this client, unioned with a small starter set
    /// (the examples the product spec gave) so a brand-new client with no lead data yet still has
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
        if (ClientConfigured && Lead is null) return NotFound();
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
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var resolvedSource = SourceChoice == "other" ? CustomSource?.Trim() : SourceChoice;

        try
        {
            var lead = await _leads.UpdateStatusAsync(client.Id, Id,
                new UpdateLeadStatusRequest(Status, QualificationStatus, resolvedSource), ct);
            if (lead is null) return NotFound();

            StatusMessage = "Saved.";
            return RedirectToPage(new { Id });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;

        Lead = await _leads.GetByIdAsync(client.Id, Id, ct);
        if (Lead is null) return;
        Stages = await _leads.ListStagesAsync(client.Id, ct);

        var distinctSources = await _leads.GetDistinctSourcesAsync(client.Id, ct);
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
