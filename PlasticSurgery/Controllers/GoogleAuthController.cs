using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>"Continue with Google" for signing in to / signing up for SculptFlow. Start sends the browser to
/// Google (the Google handler owns /signin-google); Callback is where it lands afterwards, with Google's identity
/// held in the short-lived external cookie. Outcomes:
///  - a Google login already linked to a user -> sign in;
///  - an existing account with the same email -> link it, then sign in;
///  - nobody yet -> CompleteGoogleSignup asks for the clinic name (a signup always creates a NEW clinic).
/// An email is only ever trusted when Google reports it as verified.</summary>
[Route("auth/google")]
[AllowAnonymous]
public class GoogleAuthController : Controller
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleAuthController> _logger;

    public GoogleAuthController(
        SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, IConfiguration configuration,
        ILogger<GoogleAuthController> logger)
    {
        _signIn = signIn;
        _users = users;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>POST (with the antiforgery token the Login/Register forms carry), like ASP.NET Identity's own
    /// external-login button.</summary>
    [HttpPost("start")]
    [ValidateAntiForgeryToken]
    public IActionResult Start(string? returnUrl)
    {
        if (!GoogleLoginSettings.IsEnabled(_configuration)) return Redirect("/Account/Login?externalError=failed");

        var callback = Url.Action(nameof(Callback), "GoogleAuth", new { returnUrl = SafeReturnUrl(returnUrl) });
        var properties = _signIn.ConfigureExternalAuthenticationProperties(GoogleLoginSettings.Provider, callback);
        return Challenge(properties, GoogleLoginSettings.Provider);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? returnUrl)
    {
        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info is null) return Redirect("/Account/Login?externalError=failed");
        var destination = SafeReturnUrl(returnUrl);

        // 1) Already linked: this exact Google account has signed in before.
        var result = await _signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect(destination);
        }
        if (result.IsLockedOut) return await ExternalFailure("locked");

        // 2) Not linked yet: only go further with an email Google itself has verified.
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var verified = string.Equals(info.Principal.FindFirstValue(GoogleLoginSettings.EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(email) || !verified) return await ExternalFailure("unverified");

        var existing = await _users.FindByEmailAsync(email);
        if (existing is null)
        {
            // 3) Nobody with this email: collect the clinic name, then create the account (see CompleteGoogleSignup).
            return Redirect($"/Account/CompleteGoogleSignup?returnUrl={Uri.EscapeDataString(destination)}");
        }

        if (await _users.IsLockedOutAsync(existing)) return await ExternalFailure("locked");

        var linked = await _users.AddLoginAsync(existing, info);
        if (!linked.Succeeded)
        {
            _logger.LogWarning("Linking Google to existing user {UserId} failed: {Errors}", existing.Id, string.Join("; ", linked.Errors.Select(e => e.Description)));
            return await ExternalFailure("failed");
        }

        if (!existing.EmailConfirmed)
        {
            // Registration never verified this address, so whoever created the password account may not own it
            // (they could have registered with someone else's email before the real owner arrived). Google has
            // just proven the real owner, so this account becomes theirs: confirm the email and drop the old
            // password, otherwise the original registrant could still sign in to it.
            existing.EmailConfirmed = true;
            await _users.UpdateAsync(existing);
            if (await _users.HasPasswordAsync(existing)) await _users.RemovePasswordAsync(existing);
            await _users.UpdateSecurityStampAsync(existing);
            _logger.LogInformation("Linked Google to existing unverified account {UserId}; its password was removed.", existing.Id);
        }

        await _signIn.SignInAsync(existing, isPersistent: true, authenticationMethod: info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return LocalRedirect(destination);
    }

    private async Task<IActionResult> ExternalFailure(string code)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Redirect($"/Account/Login?externalError={code}");
    }

    private string SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : "/dashboard";
}
