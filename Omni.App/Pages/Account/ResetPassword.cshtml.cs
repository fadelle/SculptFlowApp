using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Services.Accounts;
using Omni.App.Common.Helpers;

namespace Omni.App.Pages.Account;

/// <summary>The page a reset email links to (?token=…): two password fields, or "this link no longer works" with a way to
/// ask for a new one. Success goes to the sign-in page (?reset=done); other sessions of the account are signed out.</summary>
public class ResetPasswordModel : PageModel
{
    private readonly IPasswordResetService _reset;
    private readonly IStringLocalizer _text;

    public ResetPasswordModel(IPasswordResetService reset, IStringLocalizer text)
    {
        _reset = reset;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    // Passwords are never echoed back into the form.
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool LinkUsable { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        LinkUsable = await _reset.IsUsableAsync(Token, ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            await _reset.ResetAsync(Token, Password, ConfirmPassword, ct);
            return LocalRedirect("/Account/Login?reset=done");
        }
        catch (InvalidOperationException)
        {
            LinkUsable = false;
        }
        catch (ArgumentException ex)
        {
            LinkUsable = true;
            ErrorMessage = _text.Localize(ex);
        }
        return Page();
    }
}
