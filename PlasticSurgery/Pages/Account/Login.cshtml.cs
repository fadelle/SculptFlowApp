using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PlasticSurgery.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;

    public LoginModel(SignInManager<IdentityUser> signInManager)
    {
        _signInManager = signInManager;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet(string? externalError)
    {
        // Set by GoogleAuthController / the Google handler as a fixed code; only known codes become text.
        ErrorMessage = PlasticSurgery.Common.Configs.GoogleLoginSettings.ErrorMessage(externalError);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter your email and password.";
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(Email, Password, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ErrorMessage = result.IsLockedOut
                ? "Too many attempts. For your security, wait a few minutes and try again."
                : "That email and password don't match. Check them and try again.";
            return Page();
        }

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
