using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase;

/// <summary>Knowledge Base → Settings. Shows every persisted retrieval/embedding setting for the logged-in
/// clinic; only chunk size, chunk overlap, top K and minimum similarity are editable (the rest are shown
/// read-only — see IKnowledgeSettingsService).</summary>
public class SettingsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IKnowledgeSettingsService _settings;

    public SettingsModel(ICurrentClinicContext clinicContext, IKnowledgeSettingsService settings)
    {
        _clinicContext = clinicContext;
        _settings = settings;
    }

    public bool ClinicConfigured { get; private set; }
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
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return Page();
        ClinicConfigured = true;

        Current = await _settings.GetAsync(clinic.Id, ct);
        ChunkSizeTokens = Current.ChunkSizeTokens;
        ChunkOverlapTokens = Current.ChunkOverlapTokens;
        TopK = Current.TopK;
        MinimumSimilarity = Current.MinimumSimilarity.ToString("0.##", CultureInfo.InvariantCulture);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        ClinicConfigured = true;

        try
        {
            if (!double.TryParse(MinimumSimilarity?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var minSimilarity))
            {
                throw new ArgumentException("Minimum similarity must be a number between 0 and 1.");
            }

            await _settings.UpdateAsync(clinic.Id,
                new UpdateKnowledgeSettingsRequest(ChunkSizeTokens, ChunkOverlapTokens, TopK, minSimilarity), ct);
            StatusMessage = "Settings saved.";
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }

        // Redisplay with the read-only values from the database and the user's attempted edits.
        Current = await _settings.GetAsync(clinic.Id, ct);
        return Page();
    }
}
