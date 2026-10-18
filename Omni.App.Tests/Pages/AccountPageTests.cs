using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Business.Services.Clients;
using Omni.App.Pages.Account;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using static Omni.App.Tests.Pages.PageKit;

namespace Omni.App.Tests.Pages;

public class AccountPageTests
{
    private readonly Mock<UserManager<AppUser>> _users =
        new(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private Mock<SignInManager<AppUser>> SignIn() =>
        new(_users.Object, Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<AppUser>>(), null!, null!, null!, null!);

    // ---- login ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("failed", "Google sign-in didn't work. Please try again.")]
    [InlineData("cancelled", "Google sign-in was cancelled.")]
    [InlineData("whatever", null)]
    [InlineData(null, null)]
    public void Login_shows_a_message_only_for_known_google_error_codes(string? code, string? expected)
    {
        var model = new LoginModel(SignIn().Object, TextKit.Localizer()).Attach();
        model.OnGet(code);
        Assert.Equal(expected, model.ErrorMessage);
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("a@x.com", "  ")]
    public async Task Login_needs_both_fields_and_does_not_call_identity(string email, string password)
    {
        var signIn = SignIn();
        var model = new LoginModel(signIn.Object, TextKit.Localizer()) { Email = email, Password = password }.Attach();
        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Equal("Enter your email and password.", model.ErrorMessage);
        signIn.Verify(s => s.PasswordSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Login_rejects_wrong_passwords_and_reports_lockouts_separately()
    {
        var signIn = SignIn();
        signIn.SetupSequence(s => s.PasswordSignInAsync("a@x.com", "bad", true, true))
            .ReturnsAsync(SignInResult.Failed).ReturnsAsync(SignInResult.LockedOut);
        var model = new LoginModel(signIn.Object, TextKit.Localizer()) { Email = "a@x.com", Password = "bad" }.Attach();

        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Contains("don't match", model.ErrorMessage);
        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Contains("Too many attempts", model.ErrorMessage);
    }

    [Fact]
    public async Task Login_refusing_an_admin_or_a_removed_member_reads_like_a_wrong_password()
    {
        // AppSignInManager answers NotAllowed for them; the page must not tell anyone the account exists.
        var signIn = SignIn();
        signIn.Setup(s => s.PasswordSignInAsync("ada@x.com", "right", true, true)).ReturnsAsync(SignInResult.NotAllowed);
        var model = new LoginModel(signIn.Object, TextKit.Localizer()) { Email = "ada@x.com", Password = "right" }.Attach();
        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Equal("That email and password don't match. Check them and try again.", model.ErrorMessage);
    }

    [Theory]
    [InlineData("/contacts", "/contacts")]
    [InlineData("https://evil.example/", "/dashboard")]
    [InlineData("//evil.example", "/dashboard")]
    [InlineData(null, "/dashboard")]
    public async Task Login_success_only_follows_local_return_urls(string? returnUrl, string expected)
    {
        var signIn = SignIn();
        signIn.Setup(s => s.PasswordSignInAsync("a@x.com", "pw", true, true)).ReturnsAsync(SignInResult.Success);
        var model = new LoginModel(signIn.Object, TextKit.Localizer()) { Email = "a@x.com", Password = "pw", ReturnUrl = returnUrl }.Attach();
        Assert.Equal(expected, RedirectTarget(await model.OnPostAsync()));
    }

    // ---- register ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Register_failure_shows_all_errors_and_signs_nobody_in()
    {
        var registration = new Mock<IClientRegistrationService>();
        registration.Setup(r => r.RegisterAsync(It.IsAny<RegisterClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientRegistrationResult.Fail("Email taken.", "Password too short."));
        var signIn = SignIn();
        var model = new RegisterModel(registration.Object, signIn.Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()) { Email = "a@x.com", Password = "x" }.Attach();

        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Email taken. Password too short.", model.ErrorMessage);
        signIn.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Register_offers_sign_in_for_a_taken_email_and_words_refusals_in_the_default_words()
    {
        var registration = new Mock<IClientRegistrationService>();
        registration.Setup(r => r.RegisterAsync(It.IsAny<RegisterClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientRegistrationResult.Fail("signup.errors.clientNameTooLong", "Someone already signs in with a@x.com.") with { EmailTaken = true });
        var model = new RegisterModel(registration.Object, SignIn().Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach();

        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.True(model.EmailTaken);
        Assert.Equal($"The business name must be {ClientRegistrationService.MaxNameLength} characters or fewer. Someone already signs in with a@x.com.",
            model.ErrorMessage);
    }

    [Fact]
    public async Task Register_success_signs_the_new_user_in_and_forwards_the_form()
    {
        var user = new AppUser { Email = "a@x.com" };
        RegisterClientRequest? seen = null;
        var registration = new Mock<IClientRegistrationService>();
        registration.Setup(r => r.RegisterAsync(It.IsAny<RegisterClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RegisterClientRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ClientRegistrationResult(true, user, null, []));
        var signIn = SignIn();
        var model = new RegisterModel(registration.Object, signIn.Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer())
        {
            FullName = "Ann", ClientName = "Glow", Email = "a@x.com", Password = "pw", ConfirmPassword = "pw", ReturnUrl = "/knowledge",
            BusinessTypeCode = TextKit.General,
        }.Attach();

        Assert.Equal("/knowledge", RedirectTarget(await model.OnPostAsync(default)));
        Assert.Equal(new RegisterClientRequest("Ann", "Glow", "a@x.com", "pw", "pw", TextKit.General), seen);
        signIn.Verify(s => s.SignInAsync(user, true, null), Times.Once);
        await new RegisterModel(registration.Object, signIn.Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach().OnGetAsync(default);
    }

    [Fact]
    public async Task Register_offers_the_active_business_types_and_localizes_a_missing_choice()
    {
        var registration = new Mock<IClientRegistrationService>();
        registration.Setup(r => r.RegisterAsync(It.IsAny<RegisterClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientRegistrationResult.Fail(ClientRegistrationService.BusinessTypeRequiredKey));
        var model = new RegisterModel(registration.Object, SignIn().Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach();

        await model.OnGetAsync(default);
        Assert.Equal([TextKit.Clinic, TextKit.General], model.BusinessTypes.Select(t => t.Code));

        var failed = new RegisterModel(registration.Object, SignIn().Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await failed.OnPostAsync(default));
        Assert.Equal("Choose what kind of business you run.", failed.ErrorMessage);
        Assert.Equal(2, failed.BusinessTypes.Count);                 // the picker is filled again
    }

    // ---- complete google signup -------------------------------------------------------------------------------

    private static ExternalLoginInfo Info(string? email, bool verified, string? name = "Gina Google")
    {
        var claims = new List<Claim>();
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
        if (verified) claims.Add(new Claim("urn:google:email_verified", "true"));
        return new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity(claims, "Google")), "Google", "key-1", "Google");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("g@x.com", false)]
    [InlineData("", true)]
    public async Task Complete_signup_refuses_unverified_or_missing_google_identities(string? email, bool verified)
    {
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(email is null ? null : Info(email, verified));
        var model = new CompleteGoogleSignupModel(signIn.Object, Mock.Of<IClientRegistrationService>(), TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach();

        Assert.Equal("/Account/Login?externalError=unverified", RedirectTarget(await model.OnGetAsync()));
        Assert.Equal("/Account/Login?externalError=unverified", RedirectTarget(await model.OnPostAsync(default)));
    }

    [Fact]
    public async Task Complete_signup_shows_the_page_with_the_verified_name_and_email()
    {
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var model = new CompleteGoogleSignupModel(signIn.Object, Mock.Of<IClientRegistrationService>(r => r.IsSignupOpen), TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await model.OnGetAsync());
        Assert.Equal(("g@x.com", "Gina Google"), (model.Email, model.FullName));
        Assert.Equal([TextKit.Clinic, TextKit.General], model.BusinessTypes.Select(t => t.Code));
    }

    [Fact]
    public async Task Complete_signup_while_signups_are_closed_drops_the_google_identity_and_says_why()
    {
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var auth = new Mock<IAuthenticationService>();
        var model = new CompleteGoogleSignupModel(signIn.Object, Mock.Of<IClientRegistrationService>(r => !r.IsSignupOpen), TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer())
            .Attach(configure: http => http.RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider());

        Assert.Equal("/Account/Login?externalError=signup_closed", RedirectTarget(await model.OnGetAsync()));
        auth.Verify(a => a.SignOutAsync(It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), IdentityConstants.ExternalScheme, It.IsAny<AuthenticationProperties?>()), Times.Once);
        Assert.NotNull(Omni.App.Common.Configs.GoogleLoginSettings.ErrorKey("signup_closed"));
    }

    [Fact]
    public void Register_page_reports_whether_signups_are_open()
    {
        Assert.True(new RegisterModel(Mock.Of<IClientRegistrationService>(r => r.IsSignupOpen), SignIn().Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).SignupOpen);
        Assert.False(new RegisterModel(Mock.Of<IClientRegistrationService>(r => !r.IsSignupOpen), SignIn().Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).SignupOpen);
    }

    [Fact]
    public async Task Complete_signup_creates_the_account_signs_in_and_clears_the_external_cookie()
    {
        var user = new AppUser { Email = "g@x.com" };
        RegisterExternalClientRequest? seen = null;
        var registration = new Mock<IClientRegistrationService>();
        registration.Setup(r => r.RegisterExternalAsync(It.IsAny<RegisterExternalClientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RegisterExternalClientRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ClientRegistrationResult(true, user, null, []));
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var auth = new Mock<IAuthenticationService>();
        var model = new CompleteGoogleSignupModel(signIn.Object, registration.Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer())
            { ClientName = "Glow", BusinessTypeCode = TextKit.General, ReturnUrl = "https://evil.example/" }
            .Attach(configure: http => http.RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider());

        Assert.Equal("/dashboard", RedirectTarget(await model.OnPostAsync(default)));       // an external return url is ignored
        Assert.Equal("Glow", seen!.ClientName);
        Assert.Equal(TextKit.General, seen.BusinessTypeCode);
        Assert.Equal("g@x.com", seen.Email);
        signIn.Verify(s => s.SignInAsync(user, true, "Google"), Times.Once);
        auth.Verify(a => a.SignOutAsync(It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), IdentityConstants.ExternalScheme, It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task Complete_signup_failure_stays_on_the_page_with_the_errors()
    {
        var registration = new Mock<IClientRegistrationService>();
        registration.Setup(r => r.RegisterExternalAsync(It.IsAny<RegisterExternalClientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientRegistrationResult.Fail("Client name is required."));
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var model = new CompleteGoogleSignupModel(signIn.Object, registration.Object, TextKit.PermissiveBusinessTypes().Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Client name is required.", model.ErrorMessage);
        Assert.Equal(2, model.BusinessTypes.Count);
        signIn.Verify(s => s.SignInAsync(It.IsAny<AppUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    // ---- logout and error -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_signs_out_on_post_and_never_on_get()
    {
        var signIn = SignIn();
        var model = new LogoutModel(signIn.Object).Attach();
        Assert.Equal("/Account/Login", RedirectTarget(model.OnGet()));
        signIn.Verify(s => s.SignOutAsync(), Times.Never);
        Assert.Equal("/Account/Login", RedirectTarget(await model.OnPostAsync()));
        signIn.Verify(s => s.SignOutAsync(), Times.Once);
    }

    [Fact]
    public void Error_page_reports_the_request_id()
    {
        var model = new Omni.App.Pages.ErrorModel().Attach(configure: http => http.TraceIdentifier = "trace-1");
        model.OnGet();
        Assert.False(string.IsNullOrEmpty(model.RequestId));
        Assert.True(model.ShowRequestId);
        Assert.False(new Omni.App.Pages.ErrorModel().ShowRequestId);
    }
}
