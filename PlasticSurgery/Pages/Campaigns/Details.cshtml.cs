using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Campaigns;
using PlasticSurgery.Entities.Responses.Campaigns;

namespace PlasticSurgery.Pages.Campaigns;

public class DetailsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly ICampaignService _campaigns;

    public DetailsModel(ICurrentClinicContext clinicContext, ICampaignService campaigns)
    {
        _clinicContext = clinicContext;
        _campaigns = campaigns;
    }

    public bool ClinicConfigured { get; private set; }
    public CampaignDetailsResponse? Details { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return Page();
        }

        ClinicConfigured = true;
        Details = await _campaigns.GetByIdAsync(clinic.Id, id, ct);
        return Details is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostSendAsync(Guid id, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            var result = await _campaigns.SendAsync(clinic.Id, id, batchSize: 20, ct);
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
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            await _campaigns.StopAsync(clinic.Id, id, ct);
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
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            await _campaigns.CancelAsync(clinic.Id, id, ct);
            StatusMessage = "Campaign cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { id });
    }
}
