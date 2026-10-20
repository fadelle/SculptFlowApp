using Omni.App.Business.Managers;

namespace Omni.App.Tests.Support;

/// <summary>UI text for tests: the app's real strings files and a localizer with a fixed language and words, plus the
/// built-in business type codes.</summary>
internal static class TextKit
{
    /// <summary>The built-in business types seeded by Database/schema.sql.</summary>
    public const string Clinic = "clinic"; // leftover-ok: the built-in business type's code
    public const string General = "general";

    /// <summary>The embedded Resources/i18n/*.json files.</summary>
    public static readonly ITextCatalog Catalog = new TextCatalog();

    public static ITextContext Context(string language = AppLanguage.English, IReadOnlyDictionary<string, string>? terms = null)
    {
        var context = new Mock<ITextContext>();
        context.SetupGet(c => c.Language).Returns(language);
        context.SetupGet(c => c.Direction).Returns(AppLanguage.Direction(language));
        context.SetupGet(c => c.Culture).Returns(AppCulture.For(language));
        context.SetupGet(c => c.Offered).Returns(Catalog.Languages);
        context.SetupGet(c => c.Terms).Returns(terms ?? TermPlaceholders.Build(DefaultTerms()));
        return context.Object;
    }

    public static IStringLocalizer Localizer(string language = AppLanguage.English, IReadOnlyDictionary<string, string>? terms = null) =>
        new JsonStringLocalizer(Catalog, Context(language, terms));

    /// <summary>A notification's title and message as an English reader sees them (default words), after the trip through
    /// the database (TextArg encoding).</summary>
    public static (string Title, string? Message) English(NotificationText text)
    {
        var localizer = Localizer();
        var args = text.Args.Select(TextArg.Encode).Select(TextArg.Decode).ToArray();
        return (localizer[text.TitleKey, args].Value, text.MessageKey is null ? null : localizer[text.MessageKey, args].Value);
    }

    /// <summary>The notification reads this title (null: any) and message in English.</summary>
    public static bool Reads(NotificationText text, string? title, string? message)
    {
        var (t, m) = English(text);
        return (title is null || t == title) && m == message;
    }

    /// <summary>An exception's message as an English reader sees it (a key with its arguments, or plain text).</summary>
    public static string English(Exception exception) => Localizer().Localize(exception);

    /// <summary>The English default words from en.json (terms.*).</summary>
    public static IReadOnlyDictionary<string, TermForms> DefaultTerms() =>
        TermName.All.ToDictionary(t => t, t => new TermForms(Catalog.Find("en", $"terms.{t}.one")!, Catalog.Find("en", $"terms.{t}.other")!));

    /// <summary>A business-type service whose types are all active and whose stages accept any key (for tests about
    /// something else).</summary>
    public static Mock<IBusinessTypeService> PermissiveBusinessTypes()
    {
        var types = new Mock<IBusinessTypeService>();
        types.Setup(t => t.IsActiveAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? code, CancellationToken _) => !string.IsNullOrWhiteSpace(code));
        types.Setup(t => t.IsStageOfClientAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string? key, CancellationToken _) => !string.IsNullOrEmpty(key));
        types.Setup(t => t.ListOptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BusinessTypeOption(Clinic, "Clinic"), new BusinessTypeOption(General, "General business")]); // leftover-ok: built-in type name
        types.Setup(t => t.ListStagesForClientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PipelineKit.General().Stages);
        types.Setup(t => t.GetPipelineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(PipelineKit.General());
        return types;
    }
}
