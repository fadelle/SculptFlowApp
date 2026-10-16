using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Services.Clients;
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

    public RegisterModel(IClientRegistrationService registration, SignInManager<IdentityUser> signInManager)
    {
        _registration = registration;
        _signInManager = signInManager;
    }

    [BindProperty]
    public string FullName { get; set; } = string.Empty;

    [BindProperty]
    public string ClientName { get; set; } = string.Empty;

    [BindProperty]
    public string Email { get; set; } = string.Empty;

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

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await _registration.RegisterAsync(
            new RegisterClientRequest(FullName, ClientName, Email, Password, ConfirmPassword), ct);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors);
            return Page();
        }

        await _signInManager.SignInAsync(result.User!, isPersistent: true);

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
