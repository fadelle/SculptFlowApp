using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Omni.App.Business.Managers;

/// <summary>
/// Identity's errors (sign-up, new member, password reset) in the request's language: each description comes from the
/// strings files (<c>identity.errors.*</c>); the code stays Identity's, so callers can still tell a duplicate email apart
/// (ClientRegistrationResult.EmailTaken). Registered with AddErrorDescriber in IdentityModule (scoped, like the localizer).
/// </summary>
public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    private readonly IStringLocalizer _text;

    public LocalizedIdentityErrorDescriber(IStringLocalizer text) => _text = text;

    private static IdentityError Error(string code, LocalizedString description) => new() { Code = code, Description = description.Value };

    public override IdentityError DefaultError() => Error(nameof(DefaultError), _text["identity.errors.default"]);

    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), _text["identity.errors.concurrency"]);

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), _text["identity.errors.passwordMismatch"]);

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), _text["identity.errors.invalidToken"]);

    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), _text["identity.errors.loginTaken"]);

    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), _text["identity.errors.invalidUserName", userName ?? ""]);

    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), _text["identity.errors.invalidEmail", email ?? ""]);

    // The user name is the email, so a taken user name reads the same as a taken email.
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), _text["identity.errors.emailTaken", userName]);

    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), _text["identity.errors.emailTaken", email]);

    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), _text["identity.errors.hasPassword"]);

    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), _text["identity.errors.passwordTooShort", length]);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), _text["identity.errors.passwordUniqueChars", uniqueChars]);

    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), _text["identity.errors.passwordSymbol"]);

    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), _text["identity.errors.passwordDigit"]);

    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), _text["identity.errors.passwordLower"]);

    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), _text["identity.errors.passwordUpper"]);
}
