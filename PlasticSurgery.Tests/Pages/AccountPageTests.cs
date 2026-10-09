using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using PlasticSurgery.Pages.Account;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using static PlasticSurgery.Tests.Pages.PageKit;

namespace PlasticSurgery.Tests.Pages;

public class AccountPageTests
{
    private readonly Mock<UserManager<IdentityUser>> _users =
        new(Mock.Of<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private Mock<SignInManager<IdentityUser>> SignIn() =>
        new(_users.Object, Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(), null!, null!, null!, null!);

    // ---- login ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("failed", "Google sign-in didn't work. Please try again.")]
    [InlineData("cancelled", "Google sign-in was cancelled.")]
    [InlineData("whatever", null)]
    [InlineData(null, null)]
    public void Login_shows_a_message_only_for_known_google_error_codes(string? code, string? expected)
    {
        var model = new LoginModel(SignIn().Object).Attach();
        model.OnGet(code);
        Assert.Equal(expected, model.ErrorMessage);
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("a@x.com", "  ")]
    public async Task Login_needs_both_fields_and_does_not_call_identity(string email, string password)
    {
        var signIn = SignIn();
        var model = new LoginModel(signIn.Object) { Email = email, Password = password }.Attach();
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
        var model = new LoginModel(signIn.Object) { Email = "a@x.com", Password = "bad" }.Attach();

        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Contains("don't match", model.ErrorMessage);
        Assert.IsType<PageResult>(await model.OnPostAsync());
        Assert.Contains("Too many attempts", model.ErrorMessage);
    }

    [Theory]
    [InlineData("/leads", "/leads")]
    [InlineData("https://evil.example/", "/dashboard")]
    [InlineData("//evil.example", "/dashboard")]
    [InlineData(null, "/dashboard")]
    public async Task Login_success_only_follows_local_return_urls(string? returnUrl, string expected)
    {
        var signIn = SignIn();
        signIn.Setup(s => s.PasswordSignInAsync("a@x.com", "pw", true, true)).ReturnsAsync(SignInResult.Success);
        var model = new LoginModel(signIn.Object) { Email = "a@x.com", Password = "pw", ReturnUrl = returnUrl }.Attach();
        Assert.Equal(expected, RedirectTarget(await model.OnPostAsync()));
    }

    // ---- register ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Register_failure_shows_all_errors_and_signs_nobody_in()
    {
        var registration = new Mock<IClinicRegistrationService>();
        registration.Setup(r => r.RegisterAsync(It.IsAny<RegisterClinicRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClinicRegistrationResult.Fail("Email taken.", "Password too short."));
        var signIn = SignIn();
        var model = new RegisterModel(registration.Object, signIn.Object) { Email = "a@x.com", Password = "x" }.Attach();

        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Email taken. Password too short.", model.ErrorMessage);
        signIn.Verify(s => s.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Register_success_signs_the_new_user_in_and_forwards_the_form()
    {
        var user = new IdentityUser { Email = "a@x.com" };
        RegisterClinicRequest? seen = null;
        var registration = new Mock<IClinicRegistrationService>();
        registration.Setup(r => r.RegisterAsync(It.IsAny<RegisterClinicRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RegisterClinicRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ClinicRegistrationResult(true, user, null, []));
        var signIn = SignIn();
        var model = new RegisterModel(registration.Object, signIn.Object)
        {
            FullName = "Ann", ClinicName = "Glow", Email = "a@x.com", Password = "pw", ConfirmPassword = "pw", ReturnUrl = "/knowledge",
        }.Attach();

        Assert.Equal("/knowledge", RedirectTarget(await model.OnPostAsync(default)));
        Assert.Equal(new RegisterClinicRequest("Ann", "Glow", "a@x.com", "pw", "pw"), seen);
        signIn.Verify(s => s.SignInAsync(user, true, null), Times.Once);
        new RegisterModel(registration.Object, signIn.Object).Attach().OnGet();
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
        var model = new CompleteGoogleSignupModel(signIn.Object, Mock.Of<IClinicRegistrationService>()).Attach();

        Assert.Equal("/Account/Login?externalError=unverified", RedirectTarget(await model.OnGetAsync()));
        Assert.Equal("/Account/Login?externalError=unverified", RedirectTarget(await model.OnPostAsync(default)));
    }

    [Fact]
    public async Task Complete_signup_shows_the_page_with_the_verified_name_and_email()
    {
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var model = new CompleteGoogleSignupModel(signIn.Object, Mock.Of<IClinicRegistrationService>()).Attach();
        Assert.IsType<PageResult>(await model.OnGetAsync());
        Assert.Equal(("g@x.com", "Gina Google"), (model.Email, model.FullName));
    }

    [Fact]
    public async Task Complete_signup_creates_the_account_signs_in_and_clears_the_external_cookie()
    {
        var user = new IdentityUser { Email = "g@x.com" };
        RegisterExternalClinicRequest? seen = null;
        var registration = new Mock<IClinicRegistrationService>();
        registration.Setup(r => r.RegisterExternalAsync(It.IsAny<RegisterExternalClinicRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RegisterExternalClinicRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ClinicRegistrationResult(true, user, null, []));
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var auth = new Mock<IAuthenticationService>();
        var model = new CompleteGoogleSignupModel(signIn.Object, registration.Object) { ClinicName = "Glow", ReturnUrl = "https://evil.example/" }
            .Attach(configure: http => http.RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider());

        Assert.Equal("/dashboard", RedirectTarget(await model.OnPostAsync(default)));       // an external return url is ignored
        Assert.Equal("Glow", seen!.ClinicName);
        Assert.Equal("g@x.com", seen.Email);
        signIn.Verify(s => s.SignInAsync(user, true, "Google"), Times.Once);
        auth.Verify(a => a.SignOutAsync(It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), IdentityConstants.ExternalScheme, It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task Complete_signup_failure_stays_on_the_page_with_the_errors()
    {
        var registration = new Mock<IClinicRegistrationService>();
        registration.Setup(r => r.RegisterExternalAsync(It.IsAny<RegisterExternalClinicRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClinicRegistrationResult.Fail("Clinic name is required."));
        var signIn = SignIn();
        signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("g@x.com", true));
        var model = new CompleteGoogleSignupModel(signIn.Object, registration.Object).Attach();
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Clinic name is required.", model.ErrorMessage);
        signIn.Verify(s => s.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
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
        var model = new PlasticSurgery.Pages.ErrorModel().Attach(configure: http => http.TraceIdentifier = "trace-1");
        model.OnGet();
        Assert.False(string.IsNullOrEmpty(model.RequestId));
        Assert.True(model.ShowRequestId);
        Assert.False(new PlasticSurgery.Pages.ErrorModel().ShowRequestId);
    }
}
