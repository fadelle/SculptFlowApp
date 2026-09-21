using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase;

/// <summary>Knowledge Base → Benchmark. Only the page shell: everything on it is loaded and driven through the standalone
/// /api/knowledge/benchmark API (wwwroot/js/knowledge-benchmark.js). The page needs a clinic; the API derives it again
/// server-side from the logged-in user on every call.</summary>
public class BenchmarkModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;

    public BenchmarkModel(ICurrentClinicContext clinicContext)
    {
        _clinicContext = clinicContext;
    }

    public bool ClinicConfigured { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ClinicConfigured = await _clinicContext.GetClinicAsync(ct) is not null;
    }
}
