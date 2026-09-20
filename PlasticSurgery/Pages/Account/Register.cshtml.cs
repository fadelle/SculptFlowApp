using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Account;

/// <summary>
/// Registration always creates a NEW clinic for the new user (see IClinicRegistrationService) — it can
/// never attach an account to an existing clinic. Joining an existing clinic will be a separate
/// invitation/staff flow. The page only collects the form and signs the user in; the clinic, membership
/// and default settings are created atomically by the service.
/// </summary>
public class RegisterModel : PageModel
{
    private readonly IClinicRegistrationService _registration;
    private readonly SignInManager<IdentityUser> _signInManager;

    public RegisterModel(IClinicRegistrationService registration, SignInManager<IdentityUser> signInManager)
    {
        _registration = registration;
        _signInManager = signInManager;
    }

    [BindProperty]
    public string FullName { get; set; } = string.Empty;

    [BindProperty]
    public string ClinicName { get; set; } = string.Empty;

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

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await _registration.RegisterAsync(
            new RegisterClinicRequest(FullName, ClinicName, Email, Password, ConfirmPassword), ct);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors);
            return Page();
        }

        await _signInManager.SignInAsync(result.User!, isPersistent: true);

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
