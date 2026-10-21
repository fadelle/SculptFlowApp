using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Members;
using Omni.App.Entities.Models;

namespace Omni.App.Pages.Account;

/// <summary>The page an invite email links to (?token=…): the client's name and the invited email, a name and two password
/// fields; on success the new member is signed in and lands on the dashboard. A dead link (used, cancelled, expired)
/// says so and asks for a new invite.</summary>
public class AcceptInviteModel : PageModel
{
    private readonly IInviteService _invites;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IStringLocalizer _text;

    public AcceptInviteModel(IInviteService invites, SignInManager<AppUser> signIn, IStringLocalizer text)
    {
        _invites = invites;
        _signIn = signIn;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public string? FullName { get; set; }

    // Passwords are never echoed back into the form.
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Null when the link doesn't work.</summary>
    public InviteLanding? Invite { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Invite = await _invites.GetLandingAsync(Token, ct);
        FullName ??= Invite?.FullName;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var user = await _invites.AcceptAsync(Token, FullName, Password, ConfirmPassword, ct);
            await _signIn.SignInAsync(user, isPersistent: true);
            return LocalRedirect("/dashboard");
        }
        catch (InvalidOperationException ex) when (ex.Message == "acceptInvite.errors.linkInvalid")
        {
            Invite = null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Seats (EntitlementDeniedException) and input problems: keep the form.
            ErrorMessage = _text.Localize(ex);
            Invite = await _invites.GetLandingAsync(Token, ct);
        }
        return Page();
    }
}
