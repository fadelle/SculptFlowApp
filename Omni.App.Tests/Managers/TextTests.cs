using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Controllers.Client;
using Omni.App.Controllers.Filters;
using Omni.App.Middleware;
using Omni.App.Tests.Controllers;

namespace Omni.App.Tests.Managers;

/// <summary>UI text plumbing: the strings files, placeholders, the localizer, business-type words, the request's
/// language, the browser copy and localized API errors.</summary>
public class TextTests
{
    private static readonly IReadOnlyDictionary<string, TermForms> GuestWords = new Dictionary<string, TermForms>
    {
        ["contact"] = new("guest", "guests"),
        ["client"] = new("salon", "salons"),
    };

    private static TextCatalog Catalog(string en, string? fr = null) =>
        new(fr is null ? new Dictionary<string, string> { ["en"] = en } : new Dictionary<string, string> { ["en"] = en, ["fr"] = fr });

    // ---- catalog ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_embedded_english_file_loads_and_is_the_first_language()
    {
        Assert.Equal("en", TextKit.Catalog.Languages[0]);
        Assert.True(TextKit.Catalog.Supports("en"));
        Assert.False(TextKit.Catalog.Supports("xx"));
        Assert.Equal("contacts", TextKit.Catalog.Find("en", "terms.contact.other"));
        Assert.Equal("membres", TextKit.Catalog.Find("fr", "terms.member.other"));
        Assert.Equal("Boîte de réception", TextKit.Catalog.Find("fr", "nav.inbox"));
        Assert.Null(TextKit.Catalog.Find("fr", "no.such.key"));                  // missing: the caller falls back to English
        Assert.Empty(TextKit.Catalog.All("xx"));
        Assert.Equal(12, TextKit.Catalog.Version("en").Length);
    }

    [Fact]
    public void A_catalog_lists_english_first_and_versions_each_file_by_its_content()
    {
        var catalog = Catalog("""{"a": "A"}""", """{"a": "A fr"}""");
        Assert.Equal(["en", "fr"], catalog.Languages);
        Assert.NotEqual(catalog.Version("en"), catalog.Version("fr"));
        Assert.Equal(catalog.Version("en"), Catalog("""{"a": "A"}""").Version("en"));
        Assert.Equal(string.Empty, catalog.Version("xx"));
    }

    [Theory]
    [InlineData("""["a"]""")]
    [InlineData("""{"a": 1}""")]
    [InlineData("""{"a": "x", "a": "y"}""")]
    public void A_strings_file_must_be_one_flat_object_of_unique_texts(string json)
    {
        Assert.Throws<InvalidOperationException>(() => Catalog(json));
    }

    [Fact]
    public void English_is_required()
    {
        Assert.Throws<InvalidOperationException>(() => new TextCatalog(new Dictionary<string, string> { ["fr"] = "{}" }));
    }

    [Fact]
    public void The_browser_script_sets_the_raw_strings_safely()
    {
        var catalog = Catalog("""{"x": "</script><b>{contact}</b>"}""");
        var script = catalog.Script("en");
        Assert.StartsWith("window.omniStrings = ", script);
        Assert.DoesNotContain("</script>", script);
        var json = script["window.omniStrings = ".Length..].TrimEnd().TrimEnd(';');
        Assert.Equal("</script><b>{contact}</b>", JsonSerializer.Deserialize<Dictionary<string, string>>(json)!["x"]);
        Assert.Equal("window.omniStrings = {};\n", catalog.Script("xx"));
    }

    // ---- placeholders -------------------------------------------------------------------------------------------

    [Fact]
    public void Placeholders_take_arguments_and_terms_and_leave_unknown_ones_as_written()
    {
        var terms = TermPlaceholders.Build(GuestWords);
        Assert.Equal("Add a guest to Glow (3 guests)", TextFormat.Apply("Add a {contact} to {0} ({1} {contacts})", terms, ["Glow", 3]));
        Assert.Equal("Guests of the salon", TextFormat.Apply("{Contacts} of the {client}", terms, null));
        Assert.Equal("{2} {member} {x}", TextFormat.Apply("{2} {member} {x}", terms, ["a"]));
        Assert.Equal("{0}", TextFormat.Apply("{0}", terms, [null]));
        Assert.Equal("no braces", TextFormat.Apply("no braces", terms, null));
        Assert.Equal(["0", "contact", "Contacts"], TextFormat.PlaceholdersIn("{0} {contact} {Contacts} {{"));
    }

    [Fact]
    public void Every_term_has_four_placeholders_and_capitals_follow_the_first_letter()
    {
        Assert.Equal(24, TermPlaceholders.Names.Count);
        Assert.Contains("Bookings", TermPlaceholders.Names);
        Assert.Equal("Éclair", TermPlaceholders.Capitalize("éclair"));
        Assert.Equal("مريض", TermPlaceholders.Capitalize("مريض"));
        Assert.Equal("", TermPlaceholders.Capitalize(""));
        var built = TermPlaceholders.Build(GuestWords);
        Assert.Equal(("guests", "Salon", "Salons"), (built["contacts"], built["Client"], built["Clients"]));
        Assert.False(built.ContainsKey("member"));
    }

    // ---- language -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("fr", null, "fr")]                          // the client's default wins
    [InlineData("ar", "fr", "fr")]                          // ...unless there is no file for it: then the browser
    [InlineData(null, "de-DE,fr-CA;q=0.8,en;q=0.5", "fr")] // best supported browser language, by quality
    [InlineData(null, "fr;q=0,en;q=0.1", "en")]             // q=0 means "not this one"
    [InlineData(null, "*", "en")]
    [InlineData(null, "not a header ;;", "en")]
    [InlineData(null, null, "en")]
    public void The_language_is_the_first_supported_preference(string? clientDefault, string? acceptLanguage, string expected)
    {
        Assert.Equal(expected, LanguagePicker.Pick([null, clientDefault], acceptLanguage, ["en", "fr"]));
    }

    // ---- terminology --------------------------------------------------------------------------------------------

    private static BusinessType WordsType(string json) => new() { Code = "x", TerminologyJson = json };

    private static Terminology Terminology(BusinessType? type, Client? currentClient = null, ITextCatalog? catalog = null)
    {
        var types = new Mock<IBusinessTypeRepository>();
        types.Setup(t => t.GetReadOnlyAsync("x", It.IsAny<CancellationToken>())).ReturnsAsync(type);
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetClientAsync(It.IsAny<CancellationToken>())).ReturnsAsync(currentClient);
        return new Terminology(catalog ?? TextKit.Catalog, types.Object, current.Object);
    }

    [Fact]
    public async Task Each_word_falls_back_from_the_types_language_to_the_default_then_to_english()
    {
        var catalog = Catalog(
            """{"terms.contact.one": "contact", "terms.contact.other": "contacts", "terms.member.one": "member", "terms.member.other": "members"}""",
            """{"terms.member.one": "membre", "terms.member.other": "membres"}""");
        var type = WordsType("""
            {"en": {"contact": {"one": "guest", "other": "guests"}, "member": {"one": "host", "other": "hosts"}},
             "fr": {"contact": {"one": "invité", "other": "invités"}, "member": {"one": " ", "other": "x"}}}
            """);

        var fr = await Terminology(type, catalog: catalog).ForBusinessTypeAsync("x", "fr");
        Assert.Equal(new TermForms("invité", "invités"), fr["contact"]);   // the type's French word
        Assert.Equal(new TermForms("membre", "membres"), fr["member"]);    // a blank word: the French default
        Assert.Equal(new TermForms("order", "orders"), fr["order"]);       // nothing anywhere: the term name itself

        var ar = await Terminology(type, catalog: catalog).ForBusinessTypeAsync("x", "ar");
        Assert.Equal(new TermForms("guest", "guests"), ar["contact"]);     // no Arabic at all: the type's English word
    }

    [Fact]
    public async Task Unknown_types_and_broken_json_give_the_default_words()
    {
        Assert.Equal(TextKit.DefaultTerms(), await Terminology(null).ForBusinessTypeAsync("x", "en"));
        Assert.Equal(TextKit.DefaultTerms(), await Terminology(WordsType("not json")).ForBusinessTypeAsync("x", "en"));
        Assert.Equal(TextKit.DefaultTerms(), Terminology(null).Defaults("xx"));
    }

    [Fact]
    public async Task The_current_clients_words_need_a_signed_in_client()
    {
        var type = WordsType("""{"en": {"contact": {"one": "guest", "other": "guests"}}}""");
        Assert.Equal("guests", (await Terminology(type, new Client { BusinessTypeCode = "x" }).ForCurrentClientAsync("en"))["contact"].Other);
        Assert.Equal("contacts", (await Terminology(type).ForCurrentClientAsync("en"))["contact"].Other);
    }

    // ---- request text context -----------------------------------------------------------------------------------

    private static (TextContext Context, Mock<ITerminology> Terms) RequestText(Client? client, string? acceptLanguage, ITextCatalog? catalog = null)
    {
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetClientAsync(It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var http = new DefaultHttpContext();
        if (acceptLanguage is not null) http.Request.Headers.AcceptLanguage = acceptLanguage;
        var terms = new Mock<ITerminology>();
        terms.Setup(t => t.Defaults(It.IsAny<string>())).Returns(TextKit.DefaultTerms());
        terms.Setup(t => t.ForBusinessTypeAsync("x", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GuestWords);
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.LocalizationLanguages).Returns(string.Join(",", AppLanguage.All));
        var access = client is null ? null : new UserAccess("u1", client.Id, UserRole.Client, null, null, null);
        current.Setup(c => c.GetAccessAsync(It.IsAny<CancellationToken>())).ReturnsAsync(access);
        var context = new TextContext(catalog ?? TextKit.Catalog, terms.Object, current.Object, config.Object,
            Mock.Of<Microsoft.Extensions.Hosting.IHostEnvironment>(), new HttpContextAccessor { HttpContext = http }, NullLogger<TextContext>.Instance);
        return (context, terms);
    }

    [Fact]
    public async Task Before_and_without_a_request_it_is_english_with_the_default_words()
    {
        var (context, _) = RequestText(null, null);
        Assert.Equal(("en", "ltr", "contacts"), (context.Language, context.Direction, context.Terms["contacts"]));
        await context.InitializeAsync();
        Assert.Equal(("en", "Contact"), (context.Language, context.Terms["Contact"]));
    }

    [Fact]
    public async Task A_signed_in_request_uses_the_clients_language_and_words()
    {
        var catalog = Catalog("{}", "{}");
        var (context, _) = RequestText(new Client { BusinessTypeCode = "x", DefaultLanguage = "fr" }, "en", catalog);
        await context.InitializeAsync();
        Assert.Equal(("fr", "guests"), (context.Language, context.Terms["contacts"]));
    }

    [Fact]
    public async Task A_failure_keeps_english_and_the_default_words()
    {
        var (context, terms) = RequestText(new Client { BusinessTypeCode = "x", DefaultLanguage = "en" }, null);
        terms.Setup(t => t.ForBusinessTypeAsync("x", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        await context.InitializeAsync();
        Assert.Equal(("en", "contact"), (context.Language, context.Terms["contact"]));
    }

    [Fact]
    public async Task Arabic_reads_right_to_left()
    {
        var (context, _) = RequestText(new Client { BusinessTypeCode = "x", DefaultLanguage = "ar" }, null, new TextCatalog(new Dictionary<string, string> { ["en"] = "{}", ["ar"] = "{}" }));
        await context.InitializeAsync();
        Assert.Equal(("ar", "rtl"), (context.Language, context.Direction));
    }

    // ---- localizer ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_localizer_fills_terms_and_arguments_and_falls_back_to_english_then_to_the_key()
    {
        var catalog = Catalog("""{"a": "Add {contact} {0}", "b": "Only English"}""", """{"a": "Ajouter {contact} {0}"}""");
        var fr = new JsonStringLocalizer(catalog, TextKit.Context("fr", TermPlaceholders.Build(GuestWords)));

        Assert.Equal("Ajouter guest 7", fr["a", 7].Value);
        Assert.Equal("Only English", fr["b"].Value);
        var missing = fr["Some plain message {0}.", 1];
        Assert.True(missing.ResourceNotFound);
        Assert.Equal("Some plain message {0}.", missing.Value);
        Assert.Equal(["a", "b"], fr.GetAllStrings(includeParentCultures: true).Select(s => s.Name).Order());
        Assert.Equal(["a"], fr.GetAllStrings(includeParentCultures: false).Select(s => s.Name));
        Assert.Equal("Only English", new JsonStringLocalizer<TextTests>(fr)["b"].Value);
        Assert.Equal("Ajouter guest 2", new JsonStringLocalizer<TextTests>(fr)["a", 2].Value);
        Assert.Single(new JsonStringLocalizer<TextTests>(fr).GetAllStrings(false));
    }

    [Fact]
    public void Exceptions_with_a_key_are_localized_with_their_arguments_and_others_are_shown_as_written()
    {
        var text = TextKit.Localizer();
        Assert.Equal("There is no business type 'spa'.", text.Localize(new KeyNotFoundException("businessTypes.errors.notFound").WithArgs("spa")));
        Assert.Equal("Plain words.", text.Localize(new InvalidOperationException("Plain words.")));
        Assert.Equal("businessTypes.errors.notFound", ((IStringLocalizer?)null).Localize(new KeyNotFoundException("businessTypes.errors.notFound")));
        Assert.Empty(TextError.ArgsOf(new Exception()));
    }

    private static string Render(Microsoft.AspNetCore.Html.IHtmlContent html)
    {
        using var writer = new StringWriter();
        html.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public void Html_text_encodes_the_text_and_plain_arguments_but_keeps_html_arguments()
    {
        var catalog = Catalog("""{"a": "<{contact}> until {0}, by {1}.", "b": "{1} then {0}", "c": "Only {0} and {5}"}""");
        var text = new JsonStringLocalizer(catalog, TextKit.Context("en", TermPlaceholders.Build(GuestWords)));
        var time = new Microsoft.AspNetCore.Html.HtmlString("<time>May 1</time>");

        Assert.Equal("&lt;guest&gt; until <time>May 1</time>, by &lt;b&gt;Ana&lt;/b&gt;.", Render(text.Html("a", time, "<b>Ana</b>")));
        Assert.Equal("2 then <time>May 1</time>", Render(text.Html("b", time, 2)));
        Assert.Equal("Only  and {5}", Render(text.Html("c", (object?)null)));      // a null argument is empty; a missing one stays
        Assert.Equal("no.such.key", Render(text.Html("no.such.key")));
    }

    // ---- api errors ---------------------------------------------------------------------------------------------

    private static object? ErrorBody(Exception exception, IServiceProvider? services)
    {
        var http = new DefaultHttpContext();
        if (services is not null) http.RequestServices = services;
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []) { Exception = exception };
        new ApiErrorsAttribute().OnException(context);
        var result = Assert.IsType<ObjectResult>(context.Result);
        return result.Value!.GetType().GetProperty("error")!.GetValue(result.Value);
    }

    [Fact]
    public void Api_errors_answer_keyed_messages_in_the_request_language()
    {
        var services = new ServiceCollection().AddSingleton(TextKit.Localizer()).BuildServiceProvider();
        Assert.Equal("The business type 'spa' isn't active.", ErrorBody(new InvalidOperationException("businessTypes.errors.inactive").WithArgs("spa"), services));
        Assert.Equal("Your plan doesn't include campaigns.", ErrorBody(new InvalidOperationException("Your plan doesn't include campaigns."), services));
        Assert.Equal("businessTypes.errors.inactive", ErrorBody(new InvalidOperationException("businessTypes.errors.inactive"), null));
    }

    // ---- browser copy and middleware ----------------------------------------------------------------------------

    [Fact]
    public void The_strings_script_is_cached_for_a_year_only_with_the_current_version()
    {
        var controller = new UiTextController(TextKit.Catalog).With();
        var result = Assert.IsType<ContentResult>(controller.Strings("en", TextKit.Catalog.Version("en")));
        Assert.Equal("text/javascript; charset=utf-8", result.ContentType);
        Assert.Contains("\"terms.contact.one\":\"contact\"", result.Content);
        Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());

        var stale = new UiTextController(TextKit.Catalog).With();
        stale.Strings("en", "old");
        Assert.Equal("no-cache", stale.Response.Headers.CacheControl.ToString());
        Assert.IsType<NotFoundResult>(new UiTextController(TextKit.Catalog).With().Strings("xx", null));
    }

    [Theory]
    [InlineData("/dashboard", true)]
    [InlineData("/api/platform-admin/clients", true)]
    [InlineData("/css/site.css", false)]
    [InlineData("/i18n/en.js", false)]
    [InlineData("/hubs/inbox", false)]
    public async Task The_middleware_works_out_the_text_for_pages_and_apis_only(string path, bool initialized)
    {
        var text = new Mock<ITextContext>();
        text.SetupGet(t => t.Culture).Returns(AppCulture.For("en"));
        var next = false;
        var middleware = new RequestTextMiddleware(_ => { next = true; return Task.CompletedTask; });
        var http = new DefaultHttpContext();
        http.Request.Path = path;

        await middleware.InvokeAsync(http, text.Object);

        Assert.True(next);
        text.Verify(t => t.InitializeAsync(It.IsAny<CancellationToken>()), initialized ? Times.Once() : Times.Never());
    }

    // ---- stage display ------------------------------------------------------------------------------------------

    [Fact]
    public void Stages_show_their_label_and_a_colour_by_category_and_unknown_keys_fall_back()
    {
        IReadOnlyList<PipelineStageOption> stages = [new("visit", StageCategory.Booked, "Visit booked"), new("member", StageCategory.Converted, "Member")];
        Assert.Equal(("Visit booked", "badge-amber"), (StageDisplay.Label(stages, "visit"), StageDisplay.CssClass(stages, "visit")));
        Assert.Equal("badge-teal", StageDisplay.CssClass(stages, "member"));
        Assert.Equal(("Not interested", "badge-gray"), (StageDisplay.Label(stages, "not_interested"), StageDisplay.CssClass(stages, "not_interested")));
        Assert.Equal("—", StageDisplay.Label(stages, null));
    }
}
