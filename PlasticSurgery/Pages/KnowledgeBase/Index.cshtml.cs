using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase;

/// <summary>Knowledge Base list — the clinic's approved information the AI agent can search. The clinic
/// always comes from the logged-in user (ICurrentClinicContext).</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IKnowledgeService _knowledge;

    public IndexModel(ICurrentClinicContext clinicContext, IKnowledgeService knowledge)
    {
        _clinicContext = clinicContext;
        _knowledge = knowledge;
    }

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<KnowledgeDocumentResponse> Documents { get; private set; } = Array.Empty<KnowledgeDocumentResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return;

        ClinicConfigured = true;
        Documents = await _knowledge.ListAsync(clinic.Id, ct);
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
}
