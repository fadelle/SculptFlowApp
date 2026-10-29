using Omni.App.Controllers.Filters;
using Omni.App.Common.Statics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Responses.Campaigns;

namespace Omni.App.Pages.Campaigns;

[RequirePlanFeature(EntitlementKeys.Campaigns)]
public class DetailsModel : PageModel
{
    private const string CompletedKey = "campaigns.send.completed";
    private const string SendingKey = "campaigns.send.sending";
    private const string StoppedKey = "campaigns.stop.done";
    private const string CancelledKey = "campaigns.cancel.done";

    private readonly ICurrentClientContext _clientContext;
    private readonly ICampaignService _campaigns;
    private readonly IStringLocalizer _text;

    public DetailsModel(ICurrentClientContext clientContext, ICampaignService campaigns, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _campaigns = campaigns;
        _text = text;
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
                    ? _text[CompletedKey, result.Succeeded, result.Failed].Value
                    : _text[SendingKey, result.Succeeded, result.Failed, result.RemainingQueued].Value;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = _text.Localize(ex);
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
            StatusMessage = _text[StoppedKey].Value;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = _text.Localize(ex);
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
            StatusMessage = _text[CancelledKey].Value;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }

        return RedirectToPage(new { id });
    }
}
