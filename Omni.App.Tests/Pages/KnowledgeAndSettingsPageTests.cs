using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using static Omni.App.Tests.Pages.PageKit;
using KbIndex = Omni.App.Pages.KnowledgeBase.IndexModel;
using KbSettings = Omni.App.Pages.KnowledgeBase.SettingsModel;
using SiteDetails = Omni.App.Pages.KnowledgeBase.Websites.DetailsModel;
using CalendarPage = Omni.App.Pages.Settings.CalendarIntegrationsModel;

namespace Omni.App.Tests.Pages;

public class KnowledgeIndexPageTests
{
    private static readonly Client Glow = SomeClient();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IWebsiteSourceService> _websites = new();
    private KbIndex Sut(Client? client = null) => new KbIndex(ClientContext(client ?? Glow).Object, _knowledge.Object, _websites.Object, TextKit.Localizer()).Attach();

    private static KnowledgeDocumentResponse Doc(string title, string sourceType, DateTimeOffset updated) =>
        Blank<KnowledgeDocumentResponse>() with { Title = title, SourceType = sourceType, UpdatedAt = updated };

    [Fact]
    public async Task The_list_merges_documents_and_websites_newest_first_and_hides_website_pages()
    {
        var now = DateTimeOffset.UtcNow;
        _knowledge.Setup(k => k.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeDocumentResponse>
        {
            Doc("Old doc", KnowledgeSourceType.Manual, now.AddDays(-5)),
            Doc("Imported page", KnowledgeSourceType.Website, now),
            Doc("New upload", KnowledgeSourceType.Upload, now.AddDays(-1)),
        });
        _websites.Setup(w => w.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsiteSourceResponse>
        {
            Blank<WebsiteSourceResponse>() with { StartUrl = "https://a.example", LastScrapedAt = now.AddDays(-2), InProgress = true },
        });

        var model = Sut();
        await model.OnGetAsync(default);

        Assert.Equal(3, model.Rows.Count);
        Assert.Equal("New upload", model.Rows[0].Document!.Title);
        Assert.NotNull(model.Rows[1].Website);
        Assert.Equal("Old doc", model.Rows[2].Document!.Title);
        Assert.True(model.AnyWebsiteInProgress);
    }

    [Fact]
    public async Task Without_a_client_the_list_is_empty()
    {
        var model = Sut(client: null!);
        var none = new KbIndex(ClientContext(null).Object, _knowledge.Object, _websites.Object, TextKit.Localizer()).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
        Assert.Empty(none.Rows);
        Assert.False(none.AnyWebsiteInProgress);
        Assert.NotNull(model);
    }

    [Fact]
    public async Task Toggling_and_deleting_documents_report_what_happened()
    {
        var id = Guid.NewGuid();
        _knowledge.SetupSequence(k => k.SetActiveAsync(Glow.Id, id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeDocumentResponse?)null).ReturnsAsync(Doc("x", "manual", DateTimeOffset.UtcNow)).ReturnsAsync(Doc("x", "manual", DateTimeOffset.UtcNow));
        _knowledge.SetupSequence(k => k.DeleteAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(false).ReturnsAsync(true);
        var model = Sut();

        await model.OnPostToggleAsync(id, true, default);
        Assert.Equal("Entry not found.", model.ErrorMessage);
        await model.OnPostToggleAsync(id, true, default);
        Assert.StartsWith("Activated", model.StatusMessage);
        await model.OnPostToggleAsync(id, false, default);
        Assert.StartsWith("Deactivated", model.StatusMessage);

        model.ErrorMessage = null;
        await model.OnPostDeleteAsync(id, default);
        Assert.Equal("Entry not found.", model.ErrorMessage);
        await model.OnPostDeleteAsync(id, default);
        Assert.Equal("Deleted.", model.StatusMessage);
    }

    [Fact]
    public async Task Website_actions_report_what_happened()
    {
        var id = Guid.NewGuid();
        _websites.SetupSequence(w => w.SetActiveAsync(Glow.Id, id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebsiteSourceResponse?)null).ReturnsAsync(Blank<WebsiteSourceResponse>()).ReturnsAsync(Blank<WebsiteSourceResponse>());
        _websites.SetupSequence(w => w.DeleteAsync(Glow.Id, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false).ReturnsAsync(true).ThrowsAsync(new ArgumentException("A crawl is running."));
        var model = Sut();

        await model.OnPostToggleWebsiteAsync(id, true, default);
        Assert.Equal("Website not found.", model.ErrorMessage);
        await model.OnPostToggleWebsiteAsync(id, true, default);
        Assert.StartsWith("Website activated", model.StatusMessage);
        await model.OnPostToggleWebsiteAsync(id, false, default);
        Assert.StartsWith("Website deactivated", model.StatusMessage);

        model.ErrorMessage = null;
        await model.OnPostDeleteWebsiteAsync(id, default);
        Assert.Equal("Website not found.", model.ErrorMessage);
        await model.OnPostDeleteWebsiteAsync(id, default);
        Assert.StartsWith("Website and its imported pages deleted", model.StatusMessage);
        await model.OnPostDeleteWebsiteAsync(id, default);
        Assert.Equal("A crawl is running.", model.ErrorMessage);
    }

    [Fact]
    public async Task Every_post_without_a_client_just_redirects()
    {
        var model = new KbIndex(ClientContext(null).Object, _knowledge.Object, _websites.Object, TextKit.Localizer()).Attach();
        var id = Guid.NewGuid();
        Assert.IsType<RedirectToPageResult>(await model.OnPostToggleAsync(id, true, default));
        Assert.IsType<RedirectToPageResult>(await model.OnPostDeleteAsync(id, default));
        Assert.IsType<RedirectToPageResult>(await model.OnPostToggleWebsiteAsync(id, true, default));
        Assert.IsType<RedirectToPageResult>(await model.OnPostDeleteWebsiteAsync(id, default));
        _knowledge.VerifyNoOtherCalls();
    }
}

public class KnowledgePopupTests
{
    private static readonly Client Glow = SomeClient();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IWebsiteSourceService> _websites = new();

    public KnowledgePopupTests()
    {
        _knowledge.Setup(k => k.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeDocumentResponse>());
        _websites.Setup(w => w.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsiteSourceResponse>());
    }

    private KbIndex Sut(Client? client = null, bool noClient = false) =>
        new KbIndex(ClientContext(noClient ? null : client ?? Glow).Object, _knowledge.Object, _websites.Object, TextKit.Localizer()).Attach();

    private static KnowledgeDocumentResponse Existing(string sourceType = "manual") => Blank<KnowledgeDocumentResponse>() with
    {
        Title = "Hours", Category = "general", Content = "9 to 5", IsActive = false, SourceType = sourceType, SourceUrl = "https://x", OriginalFileName = "f.pdf", FileSizeBytes = 12,
    };

    // ---- get --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, false, "manual")]
    [InlineData("upload", true, "upload")]
    [InlineData("website", true, "website")]
    [InlineData("nonsense", false, "manual")]
    public async Task A_link_opens_the_add_popup_on_its_way_in_and_a_new_entry_starts_active(string? add, bool open, string mode)
    {
        var model = Sut(); model.Add = add;
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal((open, mode, true), (model.FormOpen, model.FormMode, model.IsActive));
        Assert.Null(model.Edit);

        var none = Sut(noClient: true);
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
        Assert.False(none.ClientConfigured);
    }

    [Theory]
    [InlineData("manual", false, false)]
    [InlineData("upload", true, false)]
    [InlineData("website", false, true)]
    public async Task Editing_opens_the_popup_on_the_entry_and_its_source_kind(string sourceType, bool upload, bool website)
    {
        var id = Guid.NewGuid();
        _knowledge.Setup(k => k.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Existing(sourceType));
        var model = Sut(); model.Edit = id;
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal(("Hours", "9 to 5", false, upload, website, true), (model.Title, model.Body, model.IsActive, model.IsUpload, model.IsWebsite, model.FormOpen));
        Assert.Equal(("https://x", "f.pdf", 12L), (model.SourceUrl, model.OriginalFileName, model.FileSizeBytes));

        var missing = Sut(); missing.Edit = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await missing.OnGetAsync(default));
    }

    [Fact]
    public void The_back_link_and_category_options()
    {
        _knowledge.SetupGet(k => k.MaxUploadBytes).Returns(5_000_000);
        var model = Sut();
        Assert.Equal(5_000_000, model.MaxUploadBytes);

        Assert.Equal("/KnowledgeBase", model.BackUrl);
        model.ReturnUrl = "/KnowledgeBase/Websites/1";
        Assert.Equal("/KnowledgeBase/Websites/1", model.BackUrl);
        model.ReturnUrl = "/Account/Login";                 // local but not a knowledge-base page
        Assert.Equal("/KnowledgeBase", model.BackUrl);
        model.ReturnUrl = "https://evil.example/KnowledgeBase";
        Assert.Equal("/KnowledgeBase", model.BackUrl);

        var standard = model.CategoryOptions.Count;
        model.Category = "custom-cat";
        Assert.Equal(standard + 1, model.CategoryOptions.Count);       // an unknown category is kept so it isn't lost on save
        model.Category = KnowledgeCategory.All[0];
        Assert.Equal(standard, model.CategoryOptions.Count);
    }

    // ---- save -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_a_new_entry_goes_back_to_the_list()
    {
        var model = Sut(); model.Title = "FAQ"; model.Body = "text";
        var result = await model.OnPostSaveAsync(default);
        Assert.Equal("/KnowledgeBase", RedirectTarget(result));
        _knowledge.Verify(k => k.CreateAsync(Glow.Id, new SaveKnowledgeRequest("FAQ", model.Category, "text", false), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Saved.", model.StatusMessage);
    }

    [Fact]
    public async Task Saving_an_existing_entry_updates_it_and_keeps_the_text_of_uploads_and_websites()
    {
        var id = Guid.NewGuid();
        _knowledge.Setup(k => k.GetByIdAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Existing("upload"));
        _knowledge.Setup(k => k.UpdateAsync(Glow.Id, id, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Existing());
        var model = Sut(); model.Edit = id; model.Title = "Hours"; model.Body = "tampered"; model.ReturnUrl = "/KnowledgeBase/Websites/1";
        Assert.Equal("/KnowledgeBase/Websites/1", RedirectTarget(await model.OnPostSaveAsync(default)));   // back where the edit was opened
        Assert.Equal("9 to 5", model.Body);                   // an upload's text can't be edited
        Assert.True(model.IsUpload);
        _knowledge.Verify(k => k.UpdateAsync(Glow.Id, id, It.Is<SaveKnowledgeRequest>(r => r.Content == "9 to 5"), It.IsAny<CancellationToken>()), Times.Once);

        _knowledge.Setup(k => k.UpdateAsync(Glow.Id, id, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeDocumentResponse?)null);
        Assert.IsType<NotFoundResult>(await model.OnPostSaveAsync(default));

        var gone = Sut(); gone.Edit = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await gone.OnPostSaveAsync(default));
    }

    [Fact]
    public async Task Save_errors_reopen_the_popup_and_explain_indexing_failures()
    {
        _knowledge.SetupSequence(k => k.CreateAsync(Glow.Id, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Title is required."))
            .ThrowsAsync(new InvalidOperationException("embedding service is unavailable"));
        var model = Sut();
        Assert.IsType<PageResult>(await model.OnPostSaveAsync(default));
        Assert.Equal(("Title is required.", true, "manual", true), (model.FormError, model.FormOpen, model.FormMode, model.ClientConfigured));
        Assert.IsType<PageResult>(await model.OnPostSaveAsync(default));
        Assert.StartsWith("Couldn't index this entry for AI search:", model.FormError);

        Assert.Equal("page:(same)", RedirectTarget(await Sut(noClient: true).OnPostSaveAsync(default)));
    }

    // ---- website ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_website_redirects_to_its_page_and_surfaces_validation_errors()
    {
        var created = Blank<WebsiteSourceResponse>() with { Id = Guid.NewGuid() };
        _websites.SetupSequence(w => w.CreateAsync(Glow.Id, It.IsAny<CreateWebsiteSourceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created).ThrowsAsync(new ArgumentException("Invalid URL."));
        var model = Sut(); model.WebsiteUrl = "https://client.example"; model.Edit = Guid.NewGuid();   // a stray edit id is ignored

        Assert.Equal($"/KnowledgeBase/Websites/{created.Id}", RedirectTarget(await model.OnPostWebsiteAsync(default)));
        Assert.IsType<PageResult>(await model.OnPostWebsiteAsync(default));
        Assert.Equal(("Invalid URL.", "website", true), (model.FormError, model.FormMode, model.FormOpen));
        Assert.Null(model.Edit);

        Assert.Equal("page:(same)", RedirectTarget(await Sut(noClient: true).OnPostWebsiteAsync(default)));
    }

    // ---- upload -----------------------------------------------------------------------------------------------------

    private static IFormFile File(string name = "faq.txt", string content = "hello") =>
        new FormFile(new MemoryStream(Encoding.UTF8.GetBytes(content)), 0, content.Length, "UploadFile", name);

    [Fact]
    public async Task Uploads_need_a_non_empty_file()
    {
        var missing = Sut(); missing.Title = "t";
        Assert.IsType<PageResult>(await missing.OnPostUploadAsync(default));
        Assert.Equal(("Choose a file to upload.", "upload"), (missing.FormError, missing.FormMode));

        var empty = Sut(); empty.UploadFile = File(content: "");
        Assert.IsType<PageResult>(await empty.OnPostUploadAsync(default));
        Assert.Equal("The file is empty.", empty.FormError);

        Assert.Equal("page:(same)", RedirectTarget(await Sut(noClient: true).OnPostUploadAsync(default)));
    }

    [Fact]
    public async Task A_good_upload_reports_how_many_pieces_were_created()
    {
        _knowledge.Setup(k => k.CreateFromUploadAsync(Glow.Id, It.IsAny<UploadKnowledgeRequest>(), "faq.txt", It.IsAny<Stream>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<KnowledgeDocumentResponse>() with { Title = "FAQ", ChunkCount = 1 });
        var model = Sut(); model.Title = "FAQ"; model.UploadFile = File();
        Assert.Equal("page:(same)", RedirectTarget(await model.OnPostUploadAsync(default)));
        Assert.Equal("Uploaded \"FAQ\" — 1 searchable piece created.", model.StatusMessage);

        _knowledge.Setup(k => k.CreateFromUploadAsync(Glow.Id, It.IsAny<UploadKnowledgeRequest>(), "faq.txt", It.IsAny<Stream>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<KnowledgeDocumentResponse>() with { Title = "FAQ", ChunkCount = 3 });
        model.UploadFile = File();
        await model.OnPostUploadAsync(default);
        Assert.Contains("3 searchable pieces", model.StatusMessage);
    }

    [Fact]
    public async Task Upload_failures_reopen_the_popup()
    {
        _knowledge.SetupSequence(k => k.CreateFromUploadAsync(Glow.Id, It.IsAny<UploadKnowledgeRequest>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Unsupported file type."))
            .ThrowsAsync(new InvalidOperationException("embedding service is unavailable"));
        var model = Sut(); model.UploadFile = File();
        Assert.IsType<PageResult>(await model.OnPostUploadAsync(default));
        Assert.Equal("Unsupported file type.", model.FormError);
        model.UploadFile = File();
        Assert.IsType<PageResult>(await model.OnPostUploadAsync(default));
        Assert.StartsWith("Couldn't index this document for AI search:", model.FormError);
        Assert.Equal(("upload", true), (model.FormMode, model.FormOpen));
    }
}

public class KnowledgeSettingsAndWebsitePageTests
{
    private static readonly Client Glow = SomeClient();

    private static KnowledgeSettingsResponse Settings() => new("m", 1536, "cosine", "none", 500, 50, 5, 0.3, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Settings_page_shows_the_current_values_and_formats_the_similarity()
    {
        var service = new Mock<IKnowledgeSettingsService>();
        service.Setup(s => s.GetAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
        var model = new KbSettings(ClientContext(Glow).Object, service.Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await model.OnGetAsync(default));
        Assert.Equal((500, 50, 5, "0.3"), (model.ChunkSizeTokens, model.ChunkOverlapTokens, model.TopK, model.MinimumSimilarity));

        var none = new KbSettings(ClientContext(null).Object, service.Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
        Assert.False(none.ClientConfigured);
    }

    [Theory]
    [InlineData("0.45", 0.45)]
    [InlineData("0,45", 0.45)]        // a comma decimal is accepted
    public async Task Saving_settings_parses_the_similarity_and_redirects(string text, double parsed)
    {
        var service = new Mock<IKnowledgeSettingsService>();
        service.Setup(s => s.UpdateAsync(Glow.Id, It.IsAny<UpdateKnowledgeSettingsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
        var model = new KbSettings(ClientContext(Glow).Object, service.Object, TextKit.Localizer()) { ChunkSizeTokens = 400, ChunkOverlapTokens = 40, TopK = 3, MinimumSimilarity = text }.Attach();
        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync(default));
        service.Verify(s => s.UpdateAsync(Glow.Id, new UpdateKnowledgeSettingsRequest(400, 40, 3, parsed), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Settings saved.", model.StatusMessage);
    }

    [Fact]
    public async Task Saving_settings_with_a_bad_number_or_rejected_values_redisplays_the_page()
    {
        var service = new Mock<IKnowledgeSettingsService>();
        service.Setup(s => s.GetAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
        var model = new KbSettings(ClientContext(Glow).Object, service.Object, TextKit.Localizer()) { MinimumSimilarity = "abc" }.Attach();
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Contains("must be a number", model.ErrorMessage);
        service.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateKnowledgeSettingsRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        service.Setup(s => s.UpdateAsync(Glow.Id, It.IsAny<UpdateKnowledgeSettingsRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Overlap too big."));
        model.MinimumSimilarity = "0.5";
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal("Overlap too big.", model.ErrorMessage);
        Assert.NotNull(model.Current);

        var none = new KbSettings(ClientContext(null).Object, service.Object, TextKit.Localizer()).Attach();
        Assert.IsType<RedirectToPageResult>(await none.OnPostAsync(default));
    }

    // ---- website details ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Website_details_list_pages_by_status_and_merge_the_in_kb_view()
    {
        var id = Guid.NewGuid();
        var sites = new Mock<IWebsiteSourceService>();
        sites.Setup(w => w.GetAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceDetailResponse>());
        sites.Setup(w => w.ListPagesAsync(Glow.Id, id, "failed", 0, 300, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsitePageResponse> { Blank<WebsitePageResponse>() });
        sites.Setup(w => w.ListPagesAsync(Glow.Id, id, WebsitePageStatus.Indexed, 0, 300, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsitePageResponse> { Blank<WebsitePageResponse>() });
        sites.Setup(w => w.ListPagesAsync(Glow.Id, id, WebsitePageStatus.Unchanged, 0, 300, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsitePageResponse> { Blank<WebsitePageResponse>(), Blank<WebsitePageResponse>() });

        var failed = new SiteDetails(ClientContext(Glow).Object, sites.Object, TextKit.Localizer()) { Id = id, Status = "failed" }.Attach();
        Assert.IsType<PageResult>(await failed.OnGetAsync(default));
        Assert.Single(failed.Pages);

        var inKb = new SiteDetails(ClientContext(Glow).Object, sites.Object, TextKit.Localizer()) { Id = id, Status = "in_kb" }.Attach();
        await inKb.OnGetAsync(default);
        Assert.Equal(3, inKb.Pages.Count);

        var missing = new SiteDetails(ClientContext(Glow).Object, sites.Object, TextKit.Localizer()) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<NotFoundResult>(await missing.OnGetAsync(default));
        var none = new SiteDetails(ClientContext(null).Object, sites.Object, TextKit.Localizer()).Attach();
        Assert.IsType<PageResult>(await none.OnGetAsync(default));
    }

    [Fact]
    public async Task Website_actions_redirect_back_with_a_message_or_404()
    {
        var id = Guid.NewGuid();
        var sites = new Mock<IWebsiteSourceService>();
        var model = new SiteDetails(ClientContext(Glow).Object, sites.Object, TextKit.Localizer()) { Id = id }.Attach();

        Assert.IsType<NotFoundResult>(await model.OnPostRescrapeAsync(default));
        Assert.IsType<NotFoundResult>(await model.OnPostToggleAsync(true, default));
        Assert.IsType<NotFoundResult>(await model.OnPostDeleteAsync(default));

        sites.Setup(w => w.RescrapeAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        sites.Setup(w => w.SetActiveAsync(Glow.Id, id, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        sites.Setup(w => w.DeleteAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<RedirectToPageResult>(await model.OnPostRescrapeAsync(default));
        Assert.StartsWith("Re-scrape started", model.StatusMessage);
        await model.OnPostToggleAsync(true, default);
        Assert.StartsWith("Website activated", model.StatusMessage);
        await model.OnPostToggleAsync(false, default);
        Assert.StartsWith("Website deactivated", model.StatusMessage);
        var redirect = Assert.IsType<RedirectResult>(await model.OnPostDeleteAsync(default));
        Assert.Equal("/KnowledgeBase", redirect.Url);
    }

    [Fact]
    public async Task Website_rescrape_and_delete_turn_argument_errors_into_a_message()
    {
        var id = Guid.NewGuid();
        var sites = new Mock<IWebsiteSourceService>();
        sites.Setup(w => w.RescrapeAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Already running."));
        sites.Setup(w => w.DeleteAsync(Glow.Id, id, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("In use."));
        var model = new SiteDetails(ClientContext(Glow).Object, sites.Object, TextKit.Localizer()) { Id = id }.Attach();

        Assert.IsType<RedirectToPageResult>(await model.OnPostRescrapeAsync(default));
        Assert.Equal("Already running.", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await model.OnPostDeleteAsync(default));
        Assert.Equal("In use.", model.ErrorMessage);

        var none = new SiteDetails(ClientContext(null).Object, sites.Object, TextKit.Localizer()) { Id = id }.Attach();
        Assert.Equal("page:/KnowledgeBase/Index", RedirectTarget(await none.OnPostRescrapeAsync(default)));
        Assert.Equal("page:/KnowledgeBase/Index", RedirectTarget(await none.OnPostToggleAsync(true, default)));
        Assert.Equal("page:/KnowledgeBase/Index", RedirectTarget(await none.OnPostDeleteAsync(default)));
    }

    // ---- calendar integrations -------------------------------------------------------------------------------------

    [Fact]
    public async Task Calendar_page_actions_report_success_and_errors()
    {
        var calendar = new Mock<ICalendarIntegrationService>();
        calendar.Setup(c => c.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegrationResponse>());
        var model = new CalendarPage(ClientContext(Glow).Object, calendar.Object, TextKit.Localizer(), EntitlementKit.All().Object).Attach();
        await model.OnGetAsync(default);
        Assert.True(model.ClientConfigured);

        await model.OnPostRefreshCalendarsAsync("google", default);
        Assert.Equal("Calendar list refreshed.", model.StatusMessage);
        calendar.Setup(c => c.RequestRefreshCalendarsAsync(Glow.Id, "google", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Not connected."));
        await model.OnPostRefreshCalendarsAsync("google", default);
        Assert.Equal("Not connected.", model.ErrorMessage);

        await model.OnPostSelectCalendarAsync("google", "cal-1", default);
        Assert.Equal("Calendar selected.", model.StatusMessage);
        calendar.Setup(c => c.SelectCalendarAsync(Glow.Id, "google", "bad", It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Unknown calendar."));
        await model.OnPostSelectCalendarAsync("google", "bad", default);
        Assert.Equal("Unknown calendar.", model.ErrorMessage);

        await model.OnPostSetSyncEnabledAsync("google", true, default);
        Assert.Equal("Sync turned on.", model.StatusMessage);
        await model.OnPostSetSyncEnabledAsync("google", false, default);
        Assert.Equal("Sync turned off.", model.StatusMessage);
        calendar.Setup(c => c.SetSyncEnabledAsync(Glow.Id, "outlook", true, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Pick a calendar first."));
        await model.OnPostSetSyncEnabledAsync("outlook", true, default);
        Assert.Equal("Pick a calendar first.", model.ErrorMessage);

        await model.OnPostDisconnectAsync("outlook", default);
        Assert.Equal("Outlook Calendar disconnected.", model.StatusMessage);
        await model.OnPostDisconnectAsync("google", default);
        Assert.Equal("Google Calendar disconnected.", model.StatusMessage);
    }

    [Fact]
    public async Task Calendar_page_without_a_client_does_nothing()
    {
        var calendar = new Mock<ICalendarIntegrationService>();
        var model = new CalendarPage(ClientContext(null).Object, calendar.Object, TextKit.Localizer(), EntitlementKit.All().Object).Attach();
        await model.OnGetAsync(default);
        Assert.False(model.ClientConfigured);
        Assert.IsType<RedirectToPageResult>(await model.OnPostRefreshCalendarsAsync("google", default));
        Assert.IsType<RedirectToPageResult>(await model.OnPostSelectCalendarAsync("google", "x", default));
        Assert.IsType<RedirectToPageResult>(await model.OnPostSetSyncEnabledAsync("google", true, default));
        Assert.IsType<RedirectToPageResult>(await model.OnPostDisconnectAsync("google", default));
        calendar.VerifyNoOtherCalls();
    }
}
