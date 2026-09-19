using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase;

/// <summary>Add / edit one Knowledge Base entry (manual entry only — no uploads). Saving re-chunks and
/// re-embeds the text via IKnowledgeService; the clinic always comes from the logged-in user.</summary>
public class EditModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IKnowledgeService _knowledge;

    public EditModel(ICurrentClinicContext clinicContext, IKnowledgeService knowledge)
    {
        _clinicContext = clinicContext;
        _knowledge = knowledge;
    }

    /// <summary>Route value — null when adding a new entry.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    public bool ClinicConfigured { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Category { get; set; } = KnowledgeCategory.General;

    [BindProperty]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public bool IsActive { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>The standard categories, plus the entry's own if it was saved with a custom one.</summary>
    public IReadOnlyList<(string Value, string Label)> CategoryOptions =>
        KnowledgeCategory.All.Any(c => c.Value == Category) || string.IsNullOrWhiteSpace(Category)
            ? KnowledgeCategory.All
            : KnowledgeCategory.All.Append((Category, KnowledgeCategory.Label(Category))).ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return Page();
        ClinicConfigured = true;

        if (Id is null)
        {
            IsActive = true;
            return Page();
        }

        var doc = await _knowledge.GetByIdAsync(clinic.Id, Id.Value, ct);
        if (doc is null) return NotFound();

        Title = doc.Title;
        Category = doc.Category;
        Body = doc.Content;
        IsActive = doc.IsActive;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage("/KnowledgeBase/Index");
        ClinicConfigured = true;

        try
        {
            var request = new SaveKnowledgeRequest(Title, Category, Body, IsActive);
            if (Id is null)
            {
                await _knowledge.CreateAsync(clinic.Id, request, ct);
            }
            else if (await _knowledge.UpdateAsync(clinic.Id, Id.Value, request, ct) is null)
            {
                return NotFound();
            }

            TempData["StatusMessage"] = "Saved.";
            return RedirectToPage("/KnowledgeBase/Index");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"Couldn't index this entry for AI search: {ex.Message}";
        }
        return Page();
    }
}
