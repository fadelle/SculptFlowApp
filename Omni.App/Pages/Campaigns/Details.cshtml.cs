using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Entities.Responses.Campaigns;

namespace Omni.App.Pages.Campaigns;

public class DetailsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly ICampaignService _campaigns;

    public DetailsModel(ICurrentClientContext clientContext, ICampaignService campaigns)
    {
        _clientContext = clientContext;
        _campaigns = campaigns;
    }

    public bool ClientConfigured { get; private set; }
    public CampaignDetailsResponse? Details { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return Page();
        }

        ClientConfigured = true;
        Details = await _campaigns.GetByIdAsync(client.Id, id, ct);
        return Details is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSendAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            var result = await _campaigns.SendAsync(client.Id, id, batchSize: 20, ct);
            StatusMessage = result is null
                ? null
                : result.CampaignCompleted
                    ? $"Campaign completed: {result.Succeeded} sent, {result.Failed} failed."
                    : $"Sending. {result.Succeeded} sent so far, {result.Failed} failed; the other {result.RemainingQueued} go out automatically in the background. Refresh to see progress.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStopAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            await _campaigns.StopAsync(client.Id, id, ct);
            StatusMessage = "Campaign stopped. Nobody else will be messaged until you resume it.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            await _campaigns.CancelAsync(client.Id, id, ct);
            StatusMessage = "Campaign cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { id });
    }
}
