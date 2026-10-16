using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;

namespace Omni.App.Pages.KnowledgeBase;

/// <summary>Knowledge Base → Settings. Shows every persisted retrieval/embedding setting for the logged-in
/// client; only chunk size, chunk overlap, top K and minimum similarity are editable (the rest are shown
/// read-only — see IKnowledgeSettingsService).</summary>
public class SettingsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IKnowledgeSettingsService _settings;

    public SettingsModel(ICurrentClientContext clientContext, IKnowledgeSettingsService settings)
    {
        _clientContext = clientContext;
        _settings = settings;
    }

    public bool ClientConfigured { get; private set; }
    public KnowledgeSettingsResponse Current { get; private set; } = null!;

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
                throw new ArgumentException("Minimum similarity must be a number between 0 and 1.");
            }

            await _settings.UpdateAsync(client.Id,
                new UpdateKnowledgeSettingsRequest(ChunkSizeTokens, ChunkOverlapTokens, TopK, minSimilarity), ct);
            StatusMessage = "Settings saved.";
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }

        // Redisplay with the read-only values from the database and the user's attempted edits.
        Current = await _settings.GetAsync(client.Id, ct);
        return Page();
    }
}
