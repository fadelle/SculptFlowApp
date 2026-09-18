using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

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
                : $"Processed {result.Processed} ({result.Succeeded} sent, {result.Failed} failed). " +
                  (result.CampaignCompleted ? "Campaign completed." : $"{result.RemainingQueued} still queued — click Send more to continue.");
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
