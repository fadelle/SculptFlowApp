using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Omni.App.Common.Configs;
using Omni.App.Common.Helpers;
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
    private readonly SignInThrottle? _throttle;
    private readonly string? _ipHeader;

    public LoginModel(SignInManager<AppUser> signInManager, IStringLocalizer text, SignInThrottle? throttle = null,
        IOptions<ProxyOptions>? proxy = null)
    {
        _signInManager = signInManager;
        _text = text;
        _throttle = throttle;
        _ipHeader = proxy?.Value.ClientIpHeader;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Arrived from a successful password reset (?reset=done).</summary>
    public bool PasswordChanged { get; private set; }

    public void OnGet(string? externalError, string? reset = null)
    {
        PasswordChanged = reset == "done";
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

        // The lockout counts per account AND address (QA t12-fix-01), so a stranger's wrong passwords can't lock the owner out
        // from their own address; Identity's own lockout still applies to an account an admin locked.
        var address = ClientIp.Resolve(HttpContext, _ipHeader);
        if (_throttle?.IsLocked(Email, address) == true)
        {
            ErrorMessage = _text[LockedOutKey].Value;
            return Page();
        }
        var result = await _signInManager.PasswordSignInAsync(Email, Password, isPersistent: true, lockoutOnFailure: _throttle is null);
        if (!result.Succeeded)
        {
            if (!result.IsLockedOut && !result.IsNotAllowed) _throttle?.Failed(Email, address);
            ErrorMessage = _text[result.IsLockedOut || _throttle?.IsLocked(Email, address) == true ? LockedOutKey : WrongPasswordKey].Value;
            return Page();
        }
        _throttle?.Succeeded(Email, address);

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
