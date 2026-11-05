using Microsoft.Extensions.Localization;
using Omni.App.Common.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Billing: the client's plan, renewal date, included credit, wallet and reserved balance, this
/// period's usage against the plan (IPlanUsageService) and recent transactions. Read-only — top-ups and plan changes are done by Omni (admin API).
/// Client from the logged-in user; nothing provider-specific is shown. Not found while billing is switched off
/// (Billing:Enabled), like its sidebar link.</summary>
public class BillingModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IBillingQueryService _billing;
    private readonly IConfigManager _config;
    private readonly IPlanUsageService _planUsage;
    private readonly IAddonService? _addons;
    private readonly IStringLocalizer? _text;

    public BillingModel(ICurrentClientContext clientContext, IBillingQueryService billing, IConfigManager config, IPlanUsageService planUsage,
        IAddonService? addons = null, IStringLocalizer? text = null)
    {
        _clientContext = clientContext;
        _billing = billing;
        _config = config;
        _planUsage = planUsage;
        _addons = addons;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public ClientBillingSummary? Summary { get; private set; }
    public PlanUsageDto? PlanUsage { get; private set; }
    /// <summary>Only the account owner chooses whether add-ons renew (it decides what the wallet pays).</summary>
    public bool CanChooseRenewal { get; private set; }
    /// <summary>What add-ons do when nobody chose (Billing:AddonsRenewByDefault).</summary>
    public bool AddonsRenewByDefault => _config.BillingAddonsRenewByDefault;
    [TempData] public string? StatusMessage { get; set; }

    public const string RenewSavedKey = "billing.addons.renewSaved";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!_config.BillingEnabled) return NotFound();
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Page();
        ClientConfigured = true;
        CanChooseRenewal = access.Role == UserRole.Client;
        Summary = await _billing.GetSummaryAsync(access.ClientId, ct);
        PlanUsage = await _planUsage.GetAsync(access.ClientId, Summary, ct);
        return Page();
    }

    /// <summary>The owner chooses whether an add-on renews with the plan: "yes", "no" or "default" (decided 2026-11-05).</summary>
    public async Task<IActionResult> OnPostAddonRenewalAsync(string code, string choice, CancellationToken ct)
    {
        if (!_config.BillingEnabled || _addons is null) return NotFound();
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return RedirectToPage();
        if (access.Role != UserRole.Client) return Forbid();
        bool? autoRenew = choice switch { "yes" => true, "no" => false, "default" => null, _ => throw new ArgumentException("choice") };
        try
        {
            await _addons.SetAutoRenewAsync(access.ClientId, code, autoRenew, BillingSource.Client, access.UserId, ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        StatusMessage = _text?[RenewSavedKey].Value;
        return RedirectToPage();
    }
}
