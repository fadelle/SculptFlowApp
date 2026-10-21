using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Accounts;

namespace Omni.App.Pages.Account;

/// <summary>"Forgot password?": asks for the email and always answers the same, whether or not an account uses it
/// (IPasswordResetService.RequestAsync decides quietly whether to send).</summary>
public class ForgotPasswordModel : PageModel
{
    private const string EmailRequiredKey = "forgotPassword.errors.emailRequired";

    private readonly IPasswordResetService _reset;
    private readonly ITextContext _textContext;
    private readonly IStringLocalizer _text;

    public ForgotPasswordModel(IPasswordResetService reset, ITextContext textContext, IStringLocalizer text)
    {
        _reset = reset;
        _textContext = textContext;
        _text = text;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    public bool Sent { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = _text[EmailRequiredKey].Value;
            return Page();
        }

        await _reset.RequestAsync(Email, $"{Request.Scheme}://{Request.Host}", _textContext.Language, ct);
        Sent = true;
        return Page();
    }
}
