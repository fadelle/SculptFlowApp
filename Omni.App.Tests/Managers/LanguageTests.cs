using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Controllers.Client;
using Omni.App.Middleware;
using Omni.App.Tests.Controllers;

namespace Omni.App.Tests.Managers;

/// <summary>Milestone B plumbing: which language a request is shown in, the display culture (Arabic with Latin digits and
/// Gregorian dates), plural forms, number formats in texts, the browser copy of a translation, review mode and the
/// language pickers' endpoint.</summary>
public class LanguageTests
{
    private const string UserId = "u1";
    private static readonly Guid ClientId = Guid.NewGuid();

    private static TextCatalog AllLanguages(string en = "{}", string ar = "{}", string fr = "{}") =>
        new(new Dictionary<string, string> { ["en"] = en, ["ar"] = ar, ["fr"] = fr });

    // ---- culture ------------------------------------------------------------------------------------------------

    [Fact]
    public void Arabic_shows_gregorian_dates_and_latin_digits()
    {
        var ar = AppCulture.For("ar");
        Assert.Equal("1,234.5", 1234.5m.ToString("N1", ar));
        Assert.Equal("-12", (-12).ToString("N0", ar));
        Assert.Equal("5 يناير 2026", new DateTime(2026, 1, 5).ToString("d MMMM yyyy", ar));
        Assert.IsType<GregorianCalendar>(ar.DateTimeFormat.Calendar);
        Assert.True(ar.IsReadOnly);
        Assert.Same(ar, AppCulture.For("ar"));
    }

    [Fact]
    public void French_and_english_use_their_own_separators_and_unknown_languages_are_english()
    {
        Assert.Equal("1 234,5", 1234.5m.ToString("N1", AppCulture.For("fr")));
        Assert.Equal("1,234.5", 1234.5m.ToString("N1", AppCulture.For("en")));
        Assert.Same(AppCulture.For("en"), AppCulture.For("de"));
        Assert.Same(AppCulture.For("en"), AppCulture.For(null));
        Assert.Equal("fr", AppCulture.LanguageOf(AppCulture.For("fr")));
        Assert.Null(AppCulture.LanguageOf(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Shown_dates_and_numbers_follow_the_ui_culture_and_machine_ones_stay_invariant()
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = AppCulture.For("fr");
            var day = new DateTime(2026, 5, 5, 14, 30, 0);
            Assert.Equal("5 mai 2026", TextCulture.Date(day, DateStyle.Date));
            Assert.Equal("5 mai 14:30", TextCulture.Date(day, DateStyle.MonthDayTime));
            Assert.Equal("12 500", 12500m.Display("#,0"));
            Assert.Equal("12 500", UiTextHelper.Amount(12500m));
            Assert.Equal("2026-05-05", day.ToString("yyyy-MM-dd"));

            CultureInfo.CurrentUICulture = AppCulture.For("ar");
            Assert.Equal("5 مايو 2026", TextCulture.Date(day, DateStyle.Date));

            CultureInfo.CurrentUICulture = new CultureInfo("de-DE"); // not a dashboard language: English
            Assert.Equal("May 5, 2026", TextCulture.Date(DateOnly.FromDateTime(day), DateStyle.Date));
            Assert.Equal("Tue, May 5, 2026", TextCulture.Date(day, DateStyle.WeekdayDate));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    // ---- plurals and formats in texts ---------------------------------------------------------------------------

    [Theory]
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 10, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 99, "many")]
    [InlineData("ar", 100, "other")]
    [InlineData("ar", 102, "other")]
    [InlineData("ar", 103, "few")]
    [InlineData("ar", 111, "many")]
    public void Plural_categories_follow_each_language(string language, long count, string category)
    {
        Assert.Equal(category, PluralRules.Category(language, count));
    }

    [Fact]
    public void A_plural_text_picks_its_form_and_falls_back_to_other()
    {
        var catalog = AllLanguages(
            en: """{"x.one": "{0} item", "x.other": "{0} items", "y.one": "{1} piece of {0}", "y.other": "{1} pieces of {0}"}""",
            ar: """{"x.two": "عنصران", "x.few": "{0} عناصر", "x.other": "{0} عنصر"}""");
        var en = new JsonStringLocalizer(catalog, TextKit.Context("en"));
        var ar = new JsonStringLocalizer(catalog, TextKit.Context("ar"));

        Assert.Equal(("1 item", "3 items"), (en.Plural("x", 1).Value, en.Plural("x", 3).Value));
        Assert.Equal("1 piece of a.pdf", en.Plural("y", 1, "a.pdf", 1).Value);
        Assert.Equal(("عنصران", "5 عناصر", "100 عنصر"), (ar.Plural("x", 2).Value, ar.Plural("x", 5).Value, ar.Plural("x", 100).Value));
        Assert.Equal("1 item", ar.Plural("x", 1).Value);       // Arabic "one" is missing everywhere but English
        Assert.Equal("x.other", ar.PluralKey("x", 0));          // no zero form anywhere: other
        Assert.Equal("x.other", new JsonStringLocalizer<LanguageTests>(ar).PluralKey("x", 0));
    }

    [Fact]
    public void Numbers_in_texts_take_the_languages_separators_and_an_optional_format()
    {
        var catalog = AllLanguages(en: """{"t": "{0:N0} of {1:N0} ({2})", "u": "{contact:N0} {0:N0}"}""");
        Assert.Equal("1,234 of 5,000 (1234)", new JsonStringLocalizer(catalog, TextKit.Context("en"))["t", 1234, 5000, 1234].Value);
        Assert.Equal("1 234 of 5 000 (1234)", new JsonStringLocalizer(catalog, TextKit.Context("fr"))["t", 1234, 5000, 1234].Value);
        Assert.Equal("{contact:N0} text", new JsonStringLocalizer(catalog, TextKit.Context("en"))["u", "text"].Value); // a term takes no format
        Assert.Equal(["0", "1", "2"], TextFormat.PlaceholdersIn("{0:N0} {1} {2:#,0}"));
    }

    [Fact]
    public void Review_mode_shows_each_texts_key()
    {
        var catalog = AllLanguages(en: """{"a.b": "Save"}""");
        var context = new Mock<ITextContext>();
        context.SetupGet(c => c.Language).Returns("en");
        context.SetupGet(c => c.Culture).Returns(AppCulture.For("en"));
        context.SetupGet(c => c.Terms).Returns(new Dictionary<string, string>());
        context.SetupGet(c => c.ShowKeys).Returns(true);
        var text = new JsonStringLocalizer(catalog, context.Object);
        Assert.Equal("Save ⟦a.b⟧", text["a.b"].Value);
        Assert.Equal("plain message", text["plain message"].Value);
    }

    [Fact]
    public void The_browser_copy_of_a_translation_carries_english_for_missing_keys()
    {
        var catalog = AllLanguages(en: """{"a": "A", "b": "B"}""", fr: """{"a": "A fr"}""");
        Assert.Contains("\"a\":\"A fr\"", catalog.Script("fr"));
        Assert.Contains("\"b\":\"B\"", catalog.Script("fr"));
        Assert.DoesNotContain("A fr", catalog.Script("en"));
        // A change to English changes the translation's version too (its browser copy includes English).
        Assert.NotEqual(catalog.Version("fr"), AllLanguages(en: """{"a": "A", "b": "B2"}""", fr: """{"a": "A fr"}""").Version("fr"));
    }

    // ---- request language ---------------------------------------------------------------------------------------

    [Fact]
    public void The_languages_setting_always_keeps_english_first()
    {
        Assert.Equal(["en", "ar", "fr"], LanguagePicker.Enabled(" ar , FR,ar "));
        Assert.Equal(["en"], LanguagePicker.Enabled(""));
        Assert.Equal(["en", "fr"], LanguagePicker.Enabled("fr,en"));
    }

    private static IConfigManager Config(string languages = "en,ar,fr", bool showKeys = false)
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.LocalizationLanguages).Returns(languages);
        config.SetupGet(c => c.LocalizationShowTextKeys).Returns(showKeys);
        return config.Object;
    }

    private static TextContext Request(UserAccess? access, Client? client, Action<HttpRequest>? request = null, IConfigManager? config = null,
        string environment = "Staging", ITextCatalog? catalog = null)
    {
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetAccessAsync(It.IsAny<CancellationToken>())).ReturnsAsync(access);
        current.Setup(c => c.GetClientAsync(It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var http = new DefaultHttpContext();
        http.Request.Path = "/dashboard";
        request?.Invoke(http.Request);
        var terms = new Mock<ITerminology>();
        terms.Setup(t => t.Defaults(It.IsAny<string>())).Returns(TextKit.DefaultTerms());
        terms.Setup(t => t.ForBusinessTypeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TextKit.DefaultTerms());
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        return new TextContext(catalog ?? AllLanguages(), terms.Object, current.Object, config ?? Config(), env.Object,
            new HttpContextAccessor { HttpContext = http }, NullLogger<TextContext>.Instance);
    }

    private static UserAccess User(string? language, string role = UserRole.Member) => new(UserId, ClientId, role, language, "Ana", "stamp");
    private static Client ClientIn(string language) => new() { Id = ClientId, BusinessTypeCode = "general", DefaultLanguage = language };

    [Theory]
    [InlineData("fr", "ar", "fr")]   // the user's own language first
    [InlineData(null, "ar", "ar")]   // then the client's default
    [InlineData("de", "fr", "fr")]   // a stored language with no strings is skipped
    public async Task A_signed_in_user_sees_their_language_then_the_clients(string? own, string clientDefault, string expected)
    {
        var context = Request(User(own), ClientIn(clientDefault), r => r.Headers.AcceptLanguage = "en");
        await context.InitializeAsync();
        Assert.Equal(expected, context.Language);
        Assert.Same(AppCulture.For(expected), context.Culture);
    }

    [Fact]
    public async Task A_language_turned_off_falls_through_to_the_next_choice()
    {
        var context = Request(User("ar"), ClientIn("ar"), r => r.Headers.AcceptLanguage = "fr", Config("en,fr"));
        await context.InitializeAsync();
        Assert.Equal("fr", context.Language);
        Assert.Equal(["en", "fr"], context.Offered);

        var off = Request(User("ar"), ClientIn("ar"), r => r.Headers.AcceptLanguage = "ar", Config("en"));
        await off.InitializeAsync();
        Assert.Equal(("en", "ltr"), (off.Language, off.Direction));
        Assert.Equal(["en"], off.Offered);
    }

    [Fact]
    public async Task A_visitor_sees_the_language_picked_on_the_sign_in_pages_then_the_browsers()
    {
        var picked = Request(null, null, r => { r.Headers.Cookie = "omni.lang=ar"; r.Headers.AcceptLanguage = "fr"; });
        await picked.InitializeAsync();
        Assert.Equal(("ar", "rtl"), (picked.Language, picked.Direction));

        var browser = Request(null, null, r => r.Headers.AcceptLanguage = "fr-CA,en;q=0.5");
        await browser.InitializeAsync();
        Assert.Equal("fr", browser.Language);
    }

    [Theory]
    [InlineData("/api/platform-admin/clients")]
    [InlineData("/api/ai/contacts/1")]
    [InlineData("/api/integrations/whatsapp/webhook")]
    public async Task Machine_calls_are_always_english(string path)
    {
        var context = Request(null, null, r => { r.Path = path; r.Headers.AcceptLanguage = "ar"; r.Headers.Cookie = "omni.lang=fr"; });
        await context.InitializeAsync();
        Assert.Equal("en", context.Language);
    }

    [Fact]
    public async Task A_signed_in_dashboard_api_call_uses_the_users_language()
    {
        var context = Request(User("fr"), ClientIn("en"), r => r.Path = "/api/contacts");
        await context.InitializeAsync();
        Assert.Equal("fr", context.Language);
    }

    [Theory]
    [InlineData(true, "Staging", "?textKeys=1", null, true)]
    [InlineData(true, "Staging", "", "omni.textkeys=1", true)]
    [InlineData(true, "Staging", "?textKeys=0", "omni.textkeys=1", false)]
    [InlineData(true, "Production", "?textKeys=1", null, false)]
    [InlineData(false, "Staging", "?textKeys=1", null, false)]
    public async Task Review_mode_needs_the_setting_a_non_production_host_and_the_switch(bool setting, string environment, string query, string? cookie, bool expected)
    {
        var context = Request(null, null, r => { r.QueryString = new QueryString(query); if (cookie is not null) r.Headers.Cookie = cookie; },
            Config(showKeys: setting), environment);
        await context.InitializeAsync();
        Assert.Equal(expected, context.ShowKeys);
    }

    [Fact]
    public async Task The_middleware_sets_the_ui_culture_and_the_review_cookie_but_never_the_culture()
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var text = new Mock<ITextContext>();
            text.SetupGet(t => t.Culture).Returns(AppCulture.For("ar"));
            CultureInfo? seen = null;
            var middleware = new RequestTextMiddleware(_ => { seen = CultureInfo.CurrentUICulture; return Task.CompletedTask; });
            var http = new DefaultHttpContext();
            http.Request.Path = "/dashboard";
            http.Request.QueryString = new QueryString("?textKeys=1");

            await middleware.InvokeAsync(http, text.Object);

            Assert.Same(AppCulture.For("ar"), seen);
            Assert.Same(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
            Assert.Contains("omni.textkeys=1", http.Response.Headers.SetCookie.ToString());
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    // ---- POST /language -----------------------------------------------------------------------------------------

    private static (LanguageController Controller, Mock<IMemberService> Members) Picker(UserAccess? access, string languages = "en,ar,fr")
    {
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetAccessAsync(It.IsAny<CancellationToken>())).ReturnsAsync(access);
        var text = new Mock<ITextContext>();
        text.SetupGet(t => t.Offered).Returns(languages.Split(','));
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.LocalizationLanguageCookieDays).Returns(30);
        var members = new Mock<IMemberService>();
        var controller = new LanguageController(current.Object, members.Object, text.Object, config.Object).With();
        controller.Url = new UrlHelper(new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor()));
        return (controller, members);
    }

    [Fact]
    public async Task A_visitor_picks_a_language_for_the_sign_in_pages()
    {
        var (controller, members) = Picker(null);
        var result = Assert.IsType<LocalRedirectResult>(await controller.Set(" AR ", "/account/login?x=1", CancellationToken.None));
        Assert.Equal("/account/login?x=1", result.Url);
        var cookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains("omni.lang=ar", cookie);
        Assert.Contains("max-age=2592000", cookie);
        Assert.Contains("httponly", cookie);
        members.Verify(m => m.SetOwnLanguageAsync(It.IsAny<UserAccess>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_signed_in_user_saves_their_own_language_or_goes_back_to_the_default()
    {
        var user = User("fr");
        var (controller, members) = Picker(user);
        await controller.Set("ar", "/dashboard", CancellationToken.None);
        members.Verify(m => m.SetOwnLanguageAsync(user, "ar", It.IsAny<CancellationToken>()), Times.Once);

        var (back, backMembers) = Picker(user);
        await back.Set("", "/dashboard", CancellationToken.None);
        backMembers.Verify(m => m.SetOwnLanguageAsync(user, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("omni.lang=;", back.Response.Headers.SetCookie.ToString()); // the visitor cookie is dropped
    }

    [Fact]
    public async Task A_language_that_is_not_offered_changes_nothing_and_only_local_pages_are_returned_to()
    {
        var (controller, members) = Picker(User(null), "en,fr");
        var result = Assert.IsType<LocalRedirectResult>(await controller.Set("ar", "https://evil.example/x", CancellationToken.None));
        Assert.Equal("/", result.Url);
        Assert.Empty(controller.Response.Headers.SetCookie.ToString());
        members.Verify(m => m.SetOwnLanguageAsync(It.IsAny<UserAccess>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
