using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Account;

/// <summary>The one extra step when someone signs up with Google: their clinic name. Google already told us who
/// they are (held in the short-lived external cookie), so nothing else is collected; the clinic, membership and
/// defaults are created by the same transaction as a normal registration (IClinicRegistrationService).</summary>
public class CompleteGoogleSignupModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly IClinicRegistrationService _registration;

    public CompleteGoogleSignupModel(SignInManager<IdentityUser> signIn, IClinicRegistrationService registration)
    {
        _signIn = signIn;
        _registration = registration;
    }

    [BindProperty]
    public string ClinicName { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? FullName { get; private set; }
    public string? Email { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var info = await LoadVerifiedAsync();
        return info is null ? Redirect("/Account/Login?externalError=unverified") : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var info = await LoadVerifiedAsync();
        if (info is null) return Redirect("/Account/Login?externalError=unverified");

        var result = await _registration.RegisterExternalAsync(new RegisterExternalClinicRequest(
            FullName, ClinicName, Email, info.LoginProvider, info.ProviderKey, info.ProviderDisplayName), ct);
        if (!result.Succeeded)
        {
            ErrorMessage = string.Join(" ", result.Errors);
            return Page();
        }

        await _signIn.SignInAsync(result.User!, isPersistent: true, authenticationMethod: info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }

    /// <summary>The Google identity from the external cookie, but only if Google verified its email — this page is
    /// reachable without an app session, so it must never act on anything less.</summary>
    private async Task<ExternalLoginInfo?> LoadVerifiedAsync()
    {
        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info is null) return null;

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var verified = string.Equals(info.Principal.FindFirstValue(GoogleLoginSettings.EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(email) || !verified) return null;

        Email = email;
        FullName = info.Principal.FindFirstValue(ClaimTypes.Name);
        return info;
    }
}
