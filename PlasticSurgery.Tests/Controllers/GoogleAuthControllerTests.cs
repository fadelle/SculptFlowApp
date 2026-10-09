using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlasticSurgery.Controllers.Client;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace PlasticSurgery.Tests.Controllers;

/// <summary>
/// The Google sign-in callback is an account-takeover surface: it links Google to existing accounts and can replace a password.
/// These tests pin down who may be linked, when the old password is dropped, and that only local return URLs are honoured.
/// </summary>
public class GoogleAuthControllerTests
{
    private readonly Mock<UserManager<IdentityUser>> _users;
    private readonly Mock<SignInManager<IdentityUser>> _signIn;
    private readonly Mock<IAuthenticationService> _auth = new();
    private readonly Mock<IUrlHelper> _url = new();

    public GoogleAuthControllerTests()
    {
        _users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _signIn = new Mock<SignInManager<IdentityUser>>(_users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(),
            null!, null!, null!, null!);
        _url.Setup(u => u.IsLocalUrl(It.IsAny<string?>())).Returns<string?>(s => s is not null && s.StartsWith('/') && !s.StartsWith("//"));
    }

    private GoogleAuthController Sut(params (string Key, string? Value)[] config)
    {
        var services = new ServiceCollection().AddSingleton(_auth.Object).BuildServiceProvider();
        return new GoogleAuthController(_signIn.Object, _users.Object, ControllerTestKit.Config(config), Mock.Of<ILogger<GoogleAuthController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } },
            Url = _url.Object,
        };
    }

    private static ExternalLoginInfo Info(string? email, bool verified)
    {
        var claims = new List<Claim>();
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        if (verified) claims.Add(new Claim("urn:google:email_verified", "true"));
        return new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity(claims, "Google")), "Google", "key-1", "Google");
    }

    private static string RedirectTarget(IActionResult result) => result switch
    {
        RedirectResult r => r.Url,
        LocalRedirectResult l => l.Url,
        _ => throw new Xunit.Sdk.XunitException("Unexpected " + result.GetType().Name),
    };

    private void LinkedLoginFails() =>
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key-1", true, true)).ReturnsAsync(SignInResult.Failed);

    [Fact]
    public void Start_sends_the_user_back_to_login_when_google_is_not_configured()
    {
        Assert.Equal("/Account/Login?externalError=failed", RedirectTarget(Sut().Start("/dashboard")));
    }

    [Fact]
    public void Start_challenges_google_when_configured()
    {
        _url.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/auth/google/callback");
        _signIn.Setup(s => s.ConfigureExternalAuthenticationProperties("Google", It.IsAny<string?>(), null)).Returns(new AuthenticationProperties());
        var result = Sut(("GoogleLogin:ClientId", "id"), ("GoogleLogin:ClientSecret", "secret")).Start("/leads");
        var challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Contains("Google", challenge.AuthenticationSchemes);
    }

    [Fact]
    public async Task Callback_without_an_external_login_is_a_failure_redirect()
    {
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync((ExternalLoginInfo?)null);
        Assert.Equal("/Account/Login?externalError=failed", RedirectTarget(await Sut().Callback("/x")));
    }

    [Fact]
    public async Task A_linked_google_account_signs_in_and_only_local_return_urls_are_followed()
    {
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key-1", true, true)).ReturnsAsync(SignInResult.Success);
        Assert.Equal("/leads", RedirectTarget(await Sut().Callback("/leads")));
        Assert.Equal("/dashboard", RedirectTarget(await Sut().Callback("https://evil.example/phish")));
        Assert.Equal("/dashboard", RedirectTarget(await Sut().Callback("//evil.example")));
        Assert.Equal("/dashboard", RedirectTarget(await Sut().Callback(null)));
    }

    [Fact]
    public async Task A_locked_out_linked_account_is_refused()
    {
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key-1", true, true)).ReturnsAsync(SignInResult.LockedOut);
        Assert.Equal("/Account/Login?externalError=locked", RedirectTarget(await Sut().Callback(null)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("a@x.com", false)]
    [InlineData("  ", true)]
    public async Task An_unverified_or_missing_email_is_never_linked(string? email, bool verified)
    {
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info(email, verified));
        LinkedLoginFails();
        Assert.Equal("/Account/Login?externalError=unverified", RedirectTarget(await Sut().Callback(null)));
        _users.Verify(u => u.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_verified_email_goes_to_complete_signup_with_an_encoded_return_url()
    {
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("new@x.com", true));
        LinkedLoginFails();
        _users.Setup(u => u.FindByEmailAsync("new@x.com")).ReturnsAsync((IdentityUser?)null);
        var target = RedirectTarget(await Sut().Callback("/a b"));
        Assert.Equal("/Account/CompleteGoogleSignup?returnUrl=%2Fa%20b", target);
    }

    [Fact]
    public async Task A_locked_out_existing_user_is_refused()
    {
        var user = new IdentityUser { Email = "a@x.com" };
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        LinkedLoginFails();
        _users.Setup(u => u.FindByEmailAsync("a@x.com")).ReturnsAsync(user);
        _users.Setup(u => u.IsLockedOutAsync(user)).ReturnsAsync(true);
        Assert.Equal("/Account/Login?externalError=locked", RedirectTarget(await Sut().Callback(null)));
    }

    [Fact]
    public async Task A_failed_link_is_reported_and_nobody_is_signed_in()
    {
        var user = new IdentityUser { Email = "a@x.com" };
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        LinkedLoginFails();
        _users.Setup(u => u.FindByEmailAsync("a@x.com")).ReturnsAsync(user);
        _users.Setup(u => u.AddLoginAsync(user, It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "dup" }));
        Assert.Equal("/Account/Login?externalError=failed", RedirectTarget(await Sut().Callback(null)));
        _signIn.Verify(s => s.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task A_confirmed_user_is_linked_and_signed_in_with_the_password_kept()
    {
        var user = new IdentityUser { Email = "a@x.com", EmailConfirmed = true };
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        LinkedLoginFails();
        _users.Setup(u => u.FindByEmailAsync("a@x.com")).ReturnsAsync(user);
        _users.Setup(u => u.AddLoginAsync(user, It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        Assert.Equal("/leads", RedirectTarget(await Sut().Callback("/leads")));
        _users.Verify(u => u.RemovePasswordAsync(It.IsAny<IdentityUser>()), Times.Never);
        _signIn.Verify(s => s.SignInAsync(user, true, "Google"), Times.Once);
    }

    [Fact]
    public async Task Linking_an_unconfirmed_account_confirms_the_email_and_drops_the_old_password()
    {
        var user = new IdentityUser { Email = "a@x.com", EmailConfirmed = false };
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        LinkedLoginFails();
        _users.Setup(u => u.FindByEmailAsync("a@x.com")).ReturnsAsync(user);
        _users.Setup(u => u.AddLoginAsync(user, It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.HasPasswordAsync(user)).ReturnsAsync(true);
        _users.Setup(u => u.RemovePasswordAsync(user)).ReturnsAsync(IdentityResult.Success);

        await Sut().Callback(null);

        Assert.True(user.EmailConfirmed);
        _users.Verify(u => u.RemovePasswordAsync(user), Times.Once);
        _users.Verify(u => u.UpdateSecurityStampAsync(user), Times.Once);
        _signIn.Verify(s => s.SignInAsync(user, true, "Google"), Times.Once);
    }

    [Fact]
    public async Task An_unconfirmed_account_without_a_password_is_confirmed_without_a_password_removal()
    {
        var user = new IdentityUser { Email = "a@x.com", EmailConfirmed = false };
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(null)).ReturnsAsync(Info("a@x.com", true));
        LinkedLoginFails();
        _users.Setup(u => u.FindByEmailAsync("a@x.com")).ReturnsAsync(user);
        _users.Setup(u => u.AddLoginAsync(user, It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.HasPasswordAsync(user)).ReturnsAsync(false);
        await Sut().Callback(null);
        Assert.True(user.EmailConfirmed);
        _users.Verify(u => u.RemovePasswordAsync(It.IsAny<IdentityUser>()), Times.Never);
    }
}
