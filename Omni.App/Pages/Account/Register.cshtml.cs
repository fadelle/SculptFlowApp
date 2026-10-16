using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Requests.Clients;

namespace Omni.App.Pages.Account;

/// <summary>
/// Registration always creates a NEW client for the new user (see IClientRegistrationService) — it can
/// never attach an account to an existing client. Joining an existing client will be a separate
/// invitation/staff flow. The page only collects the form and signs the user in; the client, membership
/// and default settings are created atomically by the service.
/// </summary>
public class RegisterModel : PageModel
{
    private readonly IClientRegistrationService _registration;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IBusinessTypeService _businessTypes;
    private readonly IStringLocalizer _text;

    public RegisterModel(IClientRegistrationService registration, SignInManager<IdentityUser> signInManager, IBusinessTypeService businessTypes,
        IStringLocalizer text)
    {
        _registration = registration;
        _signInManager = signInManager;
        _businessTypes = businessTypes;
        _text = text;
    }

    [BindProperty]
    public string FullName { get; set; } = string.Empty;

    [BindProperty]
    public string ClientName { get; set; } = string.Empty;

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    /// <summary>The kind of business (core.business_types.code); required.</summary>
    [BindProperty]
    public string BusinessTypeCode { get; set; } = string.Empty;

    /// <summary>The active business types for the picker, named in the visitor's language.</summary>
    public IReadOnlyList<BusinessTypeOption> BusinessTypes { get; private set; } = Array.Empty<BusinessTypeOption>();

    // Passwords are deliberately never echoed back into the form after a failed attempt.
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>False while Accounts:SignupEnabled is off: the page then explains that and shows no form.</summary>
    public bool SignupOpen => _registration.IsSignupOpen;

    public async Task OnGetAsync(CancellationToken ct)
    {
        BusinessTypes = await _businessTypes.ListOptionsAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await _registration.RegisterAsync(
            new RegisterClientRequest(FullName, ClientName, Email, Password, ConfirmPassword, BusinessTypeCode), ct);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors.Select(e => _text[e].Value));
            BusinessTypes = await _businessTypes.ListOptionsAsync(ct);
            return Page();
        }

        await _signInManager.SignInAsync(result.User!, isPersistent: true);

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
