using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Entities.Dtos.Campaigns;

namespace Omni.App.Pages.Campaigns;

public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly ICampaignService _campaigns;

    public IndexModel(ICurrentClientContext clientContext, ICampaignService campaigns)
    {
        _clientContext = clientContext;
        _campaigns = campaigns;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<CampaignListRow> Campaigns { get; private set; } = Array.Empty<CampaignListRow>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        Campaigns = await _campaigns.ListAsync(client.Id, ct);
    }
}
