using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Omni.App.Common.Configs;
using Omni.App.Entities.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Omni.App.Pages.Account;

public class LoginModel : PageModel
{
    private const string MissingFieldsKey = "login.errors.missingFields";
    private const string WrongPasswordKey = "login.errors.wrongPassword";
    private const string LockedOutKey = "login.errors.lockedOut";

    private readonly SignInManager<AppUser> _signInManager;
    private readonly IStringLocalizer _text;

    public LoginModel(SignInManager<AppUser> signInManager, IStringLocalizer text)
    {
        _signInManager = signInManager;
        _text = text;
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
        ErrorMessage = GoogleLoginSettings.ErrorKey(externalError) is { } key ? _text[key].Value : null;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = _text[MissingFieldsKey].Value;
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(Email, Password, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ErrorMessage = _text[result.IsLockedOut ? LockedOutKey : WrongPasswordKey].Value;
            return Page();
        }

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
