using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase.Benchmark;

/// <summary>One "Generate Test Cases" batch as its OWN page (not just an overlay): its summary, chunks sent,
/// rejected questions, n8n's raw reply, and the full list of its own test cases (edit/review/delete, run just this
/// generation). Only the page shell — everything on it is loaded and driven through the standalone
/// /api/knowledge/benchmark API (wwwroot/js/knowledge-benchmark-generation.js). The clinic always comes from the
/// logged-in user; the generation itself is re-checked server-side on every API call, so a foreign id is "not found."</summary>
public class GenerationModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;

    public GenerationModel(ICurrentClinicContext clinicContext)
    {
        _clinicContext = clinicContext;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClinicConfigured { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ClinicConfigured = await _clinicContext.GetClinicAsync(ct) is not null;
    }
}
