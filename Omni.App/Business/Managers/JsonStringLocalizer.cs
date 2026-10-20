using System.Globalization;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Business.Managers;

/// <summary>
/// IStringLocalizer over the strings files (<see cref="ITextCatalog"/>): the key's text in the request's language
/// (<see cref="ITextContext"/>), else English, with {0}-style arguments (numbers and dates in the language's culture) and
/// business-type terms filled in. An unknown key comes back as itself with ResourceNotFound set, so a plain message passed
/// through it is shown as written. A <see cref="TextArg"/> argument is put into the same language first (another key, or a
/// stored value's label). In translation review mode (<see cref="ITextContext.ShowKeys"/>) each found text ends with its
/// key. Razor pages use it as <c>@T["key"]</c> (Pages/_ViewImports.cshtml); <see cref="JsonStringLocalizer{T}"/> serves
/// IStringLocalizer&lt;T&gt;.
/// </summary>
public class JsonStringLocalizer : IStringLocalizer, ITextLanguage
{
    private readonly ITextCatalog _catalog;
    private readonly ITextContext _context;

    public JsonStringLocalizer(ITextCatalog catalog, ITextContext context)
    {
        _catalog = catalog;
        _context = context;
    }

    public string Language => _context.Language;

    public CultureInfo Culture => _context.Culture;

    public LocalizedString this[string name] => Get(name, null);

    public LocalizedString this[string name, params object[] arguments] => Get(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var keys = _catalog.All(_context.Language).Keys.AsEnumerable();
        if (includeParentCultures) keys = keys.Union(_catalog.All(AppLanguage.English).Keys);
        return keys.Distinct().Select(key => Get(key, null)).ToList();
    }

    private LocalizedString Get(string name, object?[]? arguments)
    {
        var template = _catalog.Find(_context.Language, name) ?? _catalog.Find(AppLanguage.English, name);
        if (template is null) return new LocalizedString(name, name, resourceNotFound: true);
        var text = TextFormat.Apply(template, _context.Terms, Resolve(arguments), _context.Culture);
        return new LocalizedString(name, _context.ShowKeys ? $"{text} ⟦{name}⟧" : text);
    }

    private object?[]? Resolve(object?[]? arguments) =>
        arguments?.Any(a => a is TextArg) == true
            ? arguments.Select(a => a is TextArg arg ? (arg.Key is { } key ? Get(key, null).Value : this.ValueLabel(arg.StoredValue)) : a).ToArray()
            : arguments;
}
