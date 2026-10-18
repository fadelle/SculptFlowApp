using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Contacts;

namespace Omni.App.Pages.Dashboard;

/// <summary>
/// client-facing edit page for the three contact classification fields (stage, qualification, source) — the
/// ones Campaign audiences (ICampaignAudienceService) filter on. Posts through the same
/// IContactService.UpdateStageAsync the existing POST /api/contacts/{id}/stage endpoint
/// already uses (UpdateContactStageRequest just gained an optional Source field for this — see its
/// remarks), so there's no second write path and Campaign counts see the change immediately (they
/// read live from the database, no caching).
/// </summary>
public class ContactDetailModel : PageModel
{
    private const string SavedKey = "contactDetail.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly IContactService _contacts;
    private readonly IStringLocalizer _text;

    public ContactDetailModel(ICurrentClientContext clientContext, IContactService contacts, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _contacts = contacts;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClientConfigured { get; private set; }
    public ContactResponse? Contact { get; private set; }

    /// <summary>The client's pipeline stages in order, labelled (stage badge and dropdown).</summary>
    public IReadOnlyList<PipelineStageOption> Stages { get; private set; } = Array.Empty<PipelineStageOption>();
    public IReadOnlyList<string> QualificationOptions { get; } = ContactQualification.All.OrderBy(s => s).ToList();

    /// <summary>Distinct sources already on file for this client, unioned with a small starter set
    /// (the examples the product spec gave) so a brand-new client with no contact data yet still has
    /// sensible options — not a fixed enum, since Contact.Source has none (see UpdateContactStageRequest).</summary>
    public IReadOnlyList<string> SourceOptions { get; private set; } = Array.Empty<string>();

    private static readonly string[] SuggestedSources = { "facebook", "instagram", "google", "website", "referral", "whatsapp" };

    [BindProperty]
    public string StageKey { get; set; } = string.Empty;

    [BindProperty]
    public string Qualification { get; set; } = string.Empty;

    [BindProperty]
    public string SourceChoice { get; set; } = string.Empty;

    /// <summary>Used only when SourceChoice == "other" — lets members enter a source not already in
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
        if (ClientConfigured && Contact is null) return NotFound();
        if (Contact is not null)
        {
            StageKey = Contact.StageKey;
            Qualification = Contact.Qualification;
            SourceChoice = !string.IsNullOrWhiteSpace(Contact.Source) && SourceOptions.Contains(Contact.Source, StringComparer.OrdinalIgnoreCase)
                ? Contact.Source : "other";
            CustomSource = SourceChoice == "other" ? Contact.Source : null;
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
            var contact = await _contacts.UpdateStageAsync(client.Id, Id,
                new UpdateContactStageRequest(StageKey, Qualification, resolvedSource), ct);
            if (contact is null) return NotFound();

            StatusMessage = _text[SavedKey].Value;
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

        Contact = await _contacts.GetByIdAsync(client.Id, Id, ct);
        if (Contact is null) return;
        Stages = await _contacts.ListStagesAsync(client.Id, ct);

        var distinctSources = await _contacts.GetDistinctSourcesAsync(client.Id, ct);
        var options = new List<string>(distinctSources);
        foreach (var s in SuggestedSources)
        {
            if (!options.Contains(s, StringComparer.OrdinalIgnoreCase)) options.Add(s);
        }
        if (!string.IsNullOrWhiteSpace(Contact.Source) && !options.Contains(Contact.Source, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(Contact.Source);
        }
        options.Sort(StringComparer.OrdinalIgnoreCase);
        SourceOptions = options;
    }
}
