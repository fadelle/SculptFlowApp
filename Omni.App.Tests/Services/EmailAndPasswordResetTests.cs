using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.HttpClients.Brevo;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Email;
using Omni.App.Business.Services.Accounts;
using Omni.App.Business.Services.Email;

namespace Omni.App.Tests.Services;

/// <summary>Milestone C S1: the Brevo client and email providers, account emails in the reader's language, and the
/// password reset rules (one-time hashed links, hourly limit, same answer for unknown addresses, sign-out everywhere).</summary>
public class EmailAndPasswordResetTests
{
    private static IConfiguration AppSettings(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    // ---- token codec ------------------------------------------------------------------------------------------

    [Fact]
    public void Tokens_are_random_url_safe_and_only_their_hash_is_kept()
    {
        var a = AccountTokenCodec.NewToken();
        var b = AccountTokenCodec.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        var hash = AccountTokenCodec.Hash(a)!;
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, AccountTokenCodec.Hash($"  {a} "));
        Assert.NotEqual(hash, AccountTokenCodec.Hash(b));
        Assert.Null(AccountTokenCodec.Hash(null));
        Assert.Null(AccountTokenCodec.Hash("  "));
        Assert.Null(AccountTokenCodec.Hash(new string('a', 201)));
    }

    [Fact]
    public void Links_use_the_public_address_when_it_is_set()
    {
        Assert.Equal("https://app.omni.test", AccountLinks.Origin(" https://app.omni.test/ ", "http://evil.test"));
        Assert.Equal("http://localhost:5000", AccountLinks.Origin(null, "http://localhost:5000/"));
        Assert.Equal("https://a.test/Account/ResetPassword?token=a%2Bb", AccountLinks.Build("https://a.test", AccountLinks.ResetPasswordPath, "a+b"));
    }

    // ---- Brevo ------------------------------------------------------------------------------------------------

    private static (BrevoApiClient Client, FakeHttpHandler Http) Brevo(string? key = "xkeysib-test")
    {
        var http = new FakeHttpHandler();
        return (new BrevoApiClient(new HttpClient(http), AppSettings(("Brevo:ApiKey", key), ("Brevo:ApiBaseUrl", "https://brevo.test/v3/"))), http);
    }

    private static readonly EmailMessage Message = new("ann@x.com", "Ann", "Subject", "<p>Hi</p>", "Hi", "password_reset");

    [Fact]
    public async Task Brevo_gets_the_sender_recipient_both_bodies_and_the_tag_with_the_key_in_a_header()
    {
        var (brevo, http) = Brevo();
        http.Enqueue(HttpStatusCode.Created, """{"messageId":"<abc@smtp-relay.brevo.com>"}""");

        Assert.True(brevo.IsConfigured);
        Assert.Equal("<abc@smtp-relay.brevo.com>", await brevo.SendAsync(Message, "no-reply@omni.test", "Omni"));

        var request = Assert.Single(http.Requests);
        Assert.Equal((HttpMethod.Post, "https://brevo.test/v3/smtp/email"), (request.Method, request.Uri.ToString()));
        Assert.Equal("xkeysib-test", request.Headers["api-key"]);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("no-reply@omni.test", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("Omni", root.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("ann@x.com", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("Ann", root.GetProperty("to")[0].GetProperty("name").GetString());
        Assert.Equal(("Subject", "<p>Hi</p>", "Hi"), (root.GetProperty("subject").GetString(), root.GetProperty("htmlContent").GetString(), root.GetProperty("textContent").GetString()));
        Assert.Equal("password_reset", root.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Brevo_without_a_name_sends_the_address_alone()
    {
        var (brevo, http) = Brevo();
        http.Enqueue(HttpStatusCode.Created, "{}");
        Assert.Equal(string.Empty, await brevo.SendAsync(Message with { ToName = null }, "a@omni.test", "Omni"));
        using var body = JsonDocument.Parse(http.Requests[0].Body!);
        Assert.False(body.RootElement.GetProperty("to")[0].TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Brevo_refusals_and_network_failures_become_EmailSendException_without_the_key()
    {
        var (brevo, http) = Brevo();
        http.Enqueue(HttpStatusCode.BadRequest, """{"code":"invalid_parameter","message":"sender is not valid"}""");
        var refused = await Assert.ThrowsAsync<EmailSendException>(() => brevo.SendAsync(Message, "a@omni.test", "Omni"));
        Assert.Equal(400, refused.StatusCode);
        Assert.Contains("invalid_parameter: sender is not valid", refused.Message);
        Assert.DoesNotContain("xkeysib", refused.Message);

        http.Enqueue(HttpStatusCode.InternalServerError, "<html>" + new string('x', 500));
        Assert.True((await Assert.ThrowsAsync<EmailSendException>(() => brevo.SendAsync(Message, "a@omni.test", "Omni"))).Message.Length < 300);

        http.EnqueueThrow(new HttpRequestException("boom"));
        Assert.Contains("Could not reach Brevo", (await Assert.ThrowsAsync<EmailSendException>(() => brevo.SendAsync(Message, "a@omni.test", "Omni"))).Message);

        var (unconfigured, none) = Brevo(key: " ");
        Assert.False(unconfigured.IsConfigured);
        await Assert.ThrowsAsync<EmailSendException>(() => unconfigured.SendAsync(Message, "a@omni.test", "Omni"));
        Assert.Empty(none.Requests);
    }

    [Fact]
    public async Task The_Brevo_provider_needs_a_sender_address_and_the_log_provider_sends_nothing()
    {
        var brevo = new Mock<IBrevoApiClient>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.EmailFromName).Returns("Omni");
        var provider = new BrevoEmailProvider(brevo.Object, config.Object, NullLogger<BrevoEmailProvider>.Instance);

        config.SetupGet(c => c.EmailFromAddress).Returns("");
        await Assert.ThrowsAsync<EmailSendException>(() => provider.SendAsync(Message));
        brevo.Verify(b => b.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        config.SetupGet(c => c.EmailFromAddress).Returns("hello@omni.test");
        brevo.Setup(b => b.SendAsync(Message, "hello@omni.test", "Omni", It.IsAny<CancellationToken>())).ReturnsAsync("id-1");
        Assert.Equal("id-1", await provider.SendAsync(Message));

        Assert.StartsWith("log-", await new LogEmailProvider(NullLogger<LogEmailProvider>.Instance).SendAsync(Message));
    }

    // ---- reader text and the reset email -----------------------------------------------------------------------

    private static ReaderTextFactory ReaderText(string languages = "en,ar,fr")
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.LocalizationLanguages).Returns(languages);
        var terminology = new Mock<ITerminology>();
        terminology.Setup(t => t.Defaults(It.IsAny<string>())).Returns(TextKit.DefaultTerms());
        terminology.Setup(t => t.ForBusinessTypeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TextKit.DefaultTerms());
        return new ReaderTextFactory(TextKit.Catalog, terminology.Object, config.Object);
    }

    [Fact]
    public async Task Reader_text_takes_the_first_offered_language_and_falls_back_to_english()
    {
        Assert.Equal("ar", (await ReaderText().ForAsync([null, "de", "ar", "fr"], null)).Language);
        Assert.Equal("en", (await ReaderText("en").ForAsync(["fr"], TextKit.General)).Language);
        var french = await ReaderText().ForAsync(["fr"], null);
        Assert.Equal("Le lien ne fonctionne qu’une fois, pendant 60 minutes.", french["emails.reset.expiry", 60]);
        Assert.Equal("rtl", (await ReaderText().ForAsync(["ar"], null)).Direction);
    }

    private static (AccountEmailService Service, List<EmailMessage> Sent) EmailService(string languages = "en,ar,fr")
    {
        var sent = new List<EmailMessage>();
        var provider = new Mock<IEmailProvider>();
        provider.Setup(p => p.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => sent.Add(m)).ReturnsAsync("id");
        return (new AccountEmailService(provider.Object, ReaderText(languages)), sent);
    }

    [Fact]
    public async Task The_reset_email_is_in_the_users_language_right_to_left_in_arabic_and_encodes_names()
    {
        var (service, sent) = EmailService();
        var user = new AppUser { Email = "ann@x.com", FullName = "<b>Ann</b>", Language = "ar" };
        var client = new Client { DefaultLanguage = "fr", BusinessTypeCode = TextKit.General };

        await service.SendPasswordResetAsync(user, client, "en", "https://omni.test/Account/ResetPassword?token=t1", 60);

        var email = Assert.Single(sent);
        Assert.Equal(("ann@x.com", "<b>Ann</b>", AccountEmailService.PasswordResetTag), (email.ToEmail, email.ToName, email.Tag));
        Assert.Equal("إعادة تعيين كلمة المرور في Omni", email.Subject);
        Assert.Contains("lang=\"ar\" dir=\"rtl\"", email.Html);
        Assert.Contains("&lt;b&gt;Ann&lt;/b&gt;", email.Html);
        Assert.DoesNotContain("<b>Ann</b>", email.Html);
        Assert.Contains("href=\"https://omni.test/Account/ResetPassword?token=t1\"", email.Html);
        Assert.Contains("https://omni.test/Account/ResetPassword?token=t1", email.Text);
        Assert.Contains("ann@x.com", email.Text);
    }

    [Theory]
    [InlineData(null, "fr", "en", "Réinitialiser votre mot de passe Omni")]
    [InlineData(null, null, "ar", "إعادة تعيين كلمة المرور في Omni")]
    [InlineData(null, null, null, "Reset your Omni password")]
    public async Task Without_a_user_language_the_client_default_then_the_page_language_decide(string? userLanguage, string? clientLanguage,
        string? pageLanguage, string subject)
    {
        var (service, sent) = EmailService();
        var client = clientLanguage is null ? null : new Client { DefaultLanguage = clientLanguage, BusinessTypeCode = TextKit.General };
        await service.SendPasswordResetAsync(new AppUser { Email = "a@x.com", Language = userLanguage }, client, pageLanguage, "https://l", 60);
        Assert.Equal(subject, sent[0].Subject);
    }

    // ---- password reset service ---------------------------------------------------------------------------------

    private sealed class ResetKit
    {
        public readonly Mock<UserManager<AppUser>> Users = new(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        public readonly Mock<IAccountTokenRepository> Tokens = new();
        public readonly Mock<IClientRepository> Clients = new();
        public readonly Mock<IUnitOfWork> Uow = new();
        public readonly Mock<IUnitOfWorkTransaction> Tx = new();
        public readonly Mock<IAccountEmailService> Emails = new();
        public readonly Mock<IConfigManager> Config = new();
        public readonly List<AccountToken> Added = new();
        public readonly List<string> Links = new();
        public readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public readonly AppUser User = new() { Id = "u1", Email = "ann@x.com", UserName = "ann@x.com", Role = UserRole.Member, IsActive = true };
        public string? PublicBaseUrl;

        public ResetKit()
        {
            Config.SetupGet(c => c.AccountsResetLinkMinutes).Returns(60);
            Config.SetupGet(c => c.AccountsResetEmailsPerHour).Returns(3);
            Users.Setup(u => u.FindByEmailAsync("ann@x.com")).ReturnsAsync(User);
            Users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(User);
            Users.Setup(u => u.HasPasswordAsync(User)).ReturnsAsync(true);
            Users.Setup(u => u.RemovePasswordAsync(User)).ReturnsAsync(IdentityResult.Success);
            Users.Setup(u => u.AddPasswordAsync(User, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
            Users.Setup(u => u.ResetAccessFailedCountAsync(User)).ReturnsAsync(IdentityResult.Success);
            Users.Setup(u => u.SetLockoutEndDateAsync(User, null)).ReturnsAsync(IdentityResult.Success);
            Tokens.Setup(t => t.Add(It.IsAny<AccountToken>())).Callback<AccountToken>(Added.Add);
            Tokens.Setup(t => t.ListOpenForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Tx.Object);
            Emails.Setup(e => e.SendPasswordResetAsync(It.IsAny<AppUser>(), It.IsAny<Client?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<AppUser, Client?, string?, string, int, CancellationToken>((_, _, _, link, _, _) => Links.Add(link));
        }

        public PasswordResetService Sut()
        {
            var time = new Mock<TimeProvider>();
            time.Setup(t => t.GetUtcNow()).Returns(Now);
            return new PasswordResetService(Users.Object, Tokens.Object, Clients.Object, Uow.Object, Emails.Object, Config.Object,
                AppSettings(("App:PublicBaseUrl", PublicBaseUrl)), time.Object, NullLogger<PasswordResetService>.Instance);
        }

        /// <summary>A stored link for <paramref name="raw"/> (open unless changed).</summary>
        public AccountToken Stored(string raw, string purpose = AccountTokenPurpose.PasswordReset)
        {
            var token = new AccountToken { Id = Guid.NewGuid(), Purpose = purpose, UserId = "u1", Email = "ann@x.com", TokenHash = AccountTokenCodec.Hash(raw)!, ExpiresAt = Now.AddMinutes(30), CreatedAt = Now };
            Tokens.Setup(t => t.GetByHashAsync(token.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(token);
            return token;
        }
    }

    [Fact]
    public async Task A_request_stores_a_hashed_one_hour_link_and_emails_the_raw_token()
    {
        var kit = new ResetKit { PublicBaseUrl = "https://app.omni.test" };
        var client = new Client { Id = Guid.NewGuid(), DefaultLanguage = "fr" };
        kit.Clients.Setup(c => c.GetUserAccessAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccess("u1", client.Id, UserRole.Member, null, null, null));
        kit.Clients.Setup(c => c.GetReadOnlyAsync(client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(client);

        await kit.Sut().RequestAsync(" ann@x.com ", "http://evil.test", "ar");

        var stored = Assert.Single(kit.Added);
        Assert.Equal((AccountTokenPurpose.PasswordReset, "u1", "ann@x.com", kit.Now.AddMinutes(60)), (stored.Purpose, stored.UserId, stored.Email, stored.ExpiresAt));
        var link = Assert.Single(kit.Links);
        Assert.StartsWith("https://app.omni.test/Account/ResetPassword?token=", link);
        var raw = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
        Assert.Equal(stored.TokenHash, AccountTokenCodec.Hash(raw));
        kit.Emails.Verify(e => e.SendPasswordResetAsync(kit.User, client, "ar", link, 60, It.IsAny<CancellationToken>()), Times.Once);
        kit.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("nobody@x.com")]
    [InlineData("admin")]
    [InlineData("inactive")]
    [InlineData("limit")]
    public async Task Requests_that_must_not_send_do_nothing_and_never_throw(string case_)
    {
        var kit = new ResetKit();
        var email = case_ is "not-an-email" or "nobody@x.com" ? case_ : "ann@x.com";
        if (case_ == "admin") kit.User.Role = UserRole.Admin;
        if (case_ == "inactive") kit.User.IsActive = false;
        if (case_ == "limit")
            kit.Tokens.Setup(t => t.CountForUserSinceAsync(AccountTokenPurpose.PasswordReset, "u1", kit.Now.AddHours(-1), It.IsAny<CancellationToken>())).ReturnsAsync(3);

        await kit.Sut().RequestAsync(email, "http://localhost", null);

        Assert.Empty(kit.Added);
        Assert.Empty(kit.Links);
    }

    [Fact]
    public async Task A_provider_failure_on_request_is_logged_not_shown()
    {
        var kit = new ResetKit();
        kit.Emails.Setup(e => e.SendPasswordResetAsync(It.IsAny<AppUser>(), It.IsAny<Client?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmailSendException("Brevo refused"));
        await kit.Sut().RequestAsync("ann@x.com", "http://localhost:5000", null);
        Assert.Single(kit.Added);
    }

    [Fact]
    public async Task The_portal_can_send_a_link_past_the_hourly_limit_but_never_to_an_admin()
    {
        var kit = new ResetKit();
        kit.Tokens.Setup(t => t.CountForUserSinceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(99);
        await kit.Sut().SendForUserAsync("u1", "http://localhost:5000");
        Assert.StartsWith("http://localhost:5000/Account/ResetPassword?token=", Assert.Single(kit.Links));

        kit.User.Role = UserRole.Admin;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => kit.Sut().SendForUserAsync("u1", "http://localhost:5000"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => kit.Sut().SendForUserAsync("nobody", "http://localhost:5000"));
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("expired", false)]
    [InlineData("used", false)]
    [InlineData("revoked", false)]
    [InlineData("invite", false)]
    [InlineData("inactive", false)]
    [InlineData("unknown", false)]
    public async Task A_link_is_usable_only_while_open_for_an_active_user(string case_, bool usable)
    {
        var kit = new ResetKit();
        var link = kit.Stored("raw-1", case_ == "invite" ? AccountTokenPurpose.Invite : AccountTokenPurpose.PasswordReset);
        if (case_ == "expired") link.ExpiresAt = kit.Now;
        if (case_ == "used") link.UsedAt = kit.Now.AddMinutes(-1);
        if (case_ == "revoked") link.RevokedAt = kit.Now.AddMinutes(-1);
        if (case_ == "inactive") kit.User.IsActive = false;

        Assert.Equal(usable, await kit.Sut().IsUsableAsync(case_ == "unknown" ? "raw-2" : "raw-1"));
        Assert.False(await kit.Sut().IsUsableAsync(null));
    }

    [Theory]
    [InlineData("", "", "resetPassword.errors.missingFields")]
    [InlineData("Passw0rd!", "", "resetPassword.errors.missingFields")]
    [InlineData("Passw0rd!", "Passw0rd?", "resetPassword.errors.mismatch")]
    public async Task Resetting_checks_both_fields_first(string password, string confirm, string key)
    {
        var kit = new ResetKit();
        kit.Stored("raw-1");
        Assert.Equal(key, (await Assert.ThrowsAsync<ArgumentException>(() => kit.Sut().ResetAsync("raw-1", password, confirm))).Message);
        kit.Users.Verify(u => u.AddPasswordAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_dead_link_refuses_the_reset()
    {
        var kit = new ResetKit();
        kit.Stored("raw-1").UsedAt = kit.Now.AddMinutes(-5);
        Assert.Equal("resetPassword.errors.linkInvalid",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Sut().ResetAsync("raw-1", "Passw0rd!", "Passw0rd!"))).Message);
        kit.Users.Verify(u => u.RemovePasswordAsync(It.IsAny<AppUser>()), Times.Never);
    }

    [Fact]
    public async Task A_password_the_rules_refuse_leaves_the_old_one_and_the_link_open()
    {
        var kit = new ResetKit();
        var link = kit.Stored("raw-1");
        var validator = new Mock<IPasswordValidator<AppUser>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<UserManager<AppUser>>(), kit.User, "short"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must be at least 6 characters." }));
        kit.Users.Object.PasswordValidators.Add(validator.Object);

        Assert.Contains("at least 6", (await Assert.ThrowsAsync<ArgumentException>(() => kit.Sut().ResetAsync("raw-1", "short", "short"))).Message);
        kit.Users.Verify(u => u.RemovePasswordAsync(It.IsAny<AppUser>()), Times.Never);
        Assert.Null(link.UsedAt);
    }

    [Fact]
    public async Task A_reset_sets_the_password_clears_the_lockout_closes_every_link_and_commits_once()
    {
        var kit = new ResetKit();
        var link = kit.Stored("raw-1");
        var older = new AccountToken { Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.PasswordReset, UserId = "u1" };
        kit.Tokens.Setup(t => t.ListOpenForUserAsync(AccountTokenPurpose.PasswordReset, "u1", kit.Now, It.IsAny<CancellationToken>())).ReturnsAsync([link, older]);

        await kit.Sut().ResetAsync("raw-1", "N3w-password", "N3w-password");

        kit.Users.Verify(u => u.RemovePasswordAsync(kit.User), Times.Once);
        kit.Users.Verify(u => u.AddPasswordAsync(kit.User, "N3w-password"), Times.Once);
        kit.Users.Verify(u => u.ResetAccessFailedCountAsync(kit.User), Times.Once);
        kit.Users.Verify(u => u.SetLockoutEndDateAsync(kit.User, null), Times.Once);
        Assert.True(kit.User.EmailConfirmed);
        Assert.Equal((kit.Now, (DateTimeOffset?)null), (link.UsedAt!.Value, link.RevokedAt));
        Assert.Equal(kit.Now, older.RevokedAt);
        kit.Tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failing_identity_step_aborts_without_commit()
    {
        var kit = new ResetKit();
        kit.Stored("raw-1");
        kit.Users.Setup(u => u.AddPasswordAsync(kit.User, It.IsAny<string>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "nope" }));
        await Assert.ThrowsAsync<ArgumentException>(() => kit.Sut().ResetAsync("raw-1", "N3w-password", "N3w-password"));
        kit.Tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
