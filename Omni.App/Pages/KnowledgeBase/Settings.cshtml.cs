using Omni.App.Controllers.Filters;
using Omni.App.Common.Statics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Pages.KnowledgeBase;

/// <summary>Knowledge Base → Settings. Shows every persisted retrieval/embedding setting for the logged-in
/// client; only chunk size, chunk overlap, top K and minimum similarity are editable (the rest are shown
/// read-only — see IKnowledgeSettingsService).</summary>
[RequirePlanFeature(EntitlementKeys.AiAssistant)]
public class SettingsModel : PageModel
{
    private const string SavedKey = "knowledgeSettings.saved";
    private const string ReindexStartedKey = "knowledgeSettings.reindex.started";
    private const string ReindexBusyKey = "knowledgeSettings.reindex.busy";

    private readonly ICurrentClientContext _clientContext;
    private readonly IKnowledgeSettingsService _settings;
    private readonly IStringLocalizer _text;

    private readonly IKnowledgeReindexQueue? _reindex;

    public SettingsModel(ICurrentClientContext clientContext, IKnowledgeSettingsService settings, IStringLocalizer text,
        IKnowledgeReindexQueue? reindex = null)
    {
        _reindex = reindex;
        _clientContext = clientContext;
        _settings = settings;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public KnowledgeSettingsResponse Current { get; private set; } = null!;
    /// <summary>The client's running or last re-index since the app started (QA t4-ai-15), or null.</summary>
    public KnowledgeReindexProgress? Reindex { get; private set; }

    [BindProperty]
    public int ChunkSizeTokens { get; set; }

    [BindProperty]
    public int ChunkOverlapTokens { get; set; }

    [BindProperty]
    public int TopK { get; set; }

    /// <summary>Bound as text so "0.3" and "0,3" both work regardless of server culture.</summary>
    [BindProperty]
    public string MinimumSimilarity { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return Page();
        ClientConfigured = true;

        Current = await _settings.GetAsync(client.Id, ct);
        Reindex = _reindex?.Progress(client.Id);
        ChunkSizeTokens = Current.ChunkSizeTokens;
        ChunkOverlapTokens = Current.ChunkOverlapTokens;
        TopK = Current.TopK;
        MinimumSimilarity = Current.MinimumSimilarity.ToString("0.##", CultureInfo.InvariantCulture);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage("/KnowledgeBase/Index");
        ClientConfigured = true;

        try
        {
            if (!double.TryParse(MinimumSimilarity?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var minSimilarity))
            {
                throw new ArgumentException("knowledgeSettings.errors.similarity");
            }

            await _settings.UpdateAsync(client.Id,
                new UpdateKnowledgeSettingsRequest(ChunkSizeTokens, ChunkOverlapTokens, TopK, minSimilarity), ct);
            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }

        // Redisplay with the read-only values from the database and the user's attempted edits.
        Current = await _settings.GetAsync(client.Id, ct);
        Reindex = _reindex?.Progress(client.Id);
        return Page();
    }

    /// <summary>"Re-index now": every entry re-chunked and re-embedded with the current settings, in the background.</summary>
    public async Task<IActionResult> OnPostReindexAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null || _reindex is null) return RedirectToPage("/KnowledgeBase/Index");
        StatusMessage = _text[_reindex.Start(client.Id, DateTimeOffset.UtcNow) ? ReindexStartedKey : ReindexBusyKey].Value;
        return RedirectToPage();
    }
}
