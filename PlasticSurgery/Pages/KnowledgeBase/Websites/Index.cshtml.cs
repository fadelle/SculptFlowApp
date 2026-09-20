using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Knowledge.WebScraping;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.KnowledgeBase.Websites;

/// <summary>Knowledge Base → Websites: the websites this clinic imports pages from. The clinic always comes
/// from the logged-in user.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IWebsiteSourceService _websites;

    public IndexModel(ICurrentClinicContext clinicContext, IWebsiteSourceService websites)
    {
        _clinicContext = clinicContext;
        _websites = websites;
    }

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<WebsiteSourceResponse> Sources { get; private set; } = Array.Empty<WebsiteSourceResponse>();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return;
        ClinicConfigured = true;
        Sources = await _websites.ListAsync(clinic.Id, ct);
    }
}
