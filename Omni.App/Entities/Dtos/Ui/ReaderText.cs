using System.Globalization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;

namespace Omni.App.Entities.Dtos.Ui;

/// <summary>
/// UI strings worded for one reader who is not the current request's user: an email's recipient, a contact getting an
/// automatic message. Same rules as the request localizer (the language's text, else English; {0} arguments in the
/// language's culture; business-type words), but with the reader's language and words. Made by IReaderTextFactory.
/// Read it as <c>text["key", args]</c> so the UI text guard sees the key.
/// </summary>
public sealed class ReaderText
{
    private readonly ITextCatalog _catalog;
    private readonly IReadOnlyDictionary<string, string> _terms;

    public ReaderText(ITextCatalog catalog, string language, IReadOnlyDictionary<string, string> terms)
    {
        _catalog = catalog;
        _terms = terms;
        Language = language;
    }

    public string Language { get; }

    public string Direction => AppLanguage.Direction(Language);

    public CultureInfo Culture => AppCulture.For(Language);

    public string this[string key, params object?[] args]
    {
        get
        {
            var template = _catalog.Find(Language, key) ?? _catalog.Find(AppLanguage.English, key) ?? key;
            return TextFormat.Apply(template, _terms, args, Culture);
        }
    }
}
