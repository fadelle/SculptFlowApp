using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Entities.Dtos.Campaigns;

namespace Omni.App.Pages.Campaigns;

public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly ICampaignService _campaigns;

    public IndexModel(ICurrentClinicContext clinicContext, ICampaignService campaigns)
    {
        _clinicContext = clinicContext;
        _campaigns = campaigns;
    }

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<CampaignListRow> Campaigns { get; private set; } = Array.Empty<CampaignListRow>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        Campaigns = await _campaigns.ListAsync(clinic.Id, ct);
    }
}
