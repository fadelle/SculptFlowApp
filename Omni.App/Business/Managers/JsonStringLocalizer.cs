using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Managers;

/// <summary>
/// IStringLocalizer over the strings files (<see cref="ITextCatalog"/>): the key's text in the request's language
/// (<see cref="ITextContext"/>), else English, with {0}-style arguments and business-type terms filled in. An unknown key
/// comes back as itself with ResourceNotFound set, so a plain message passed through it is shown as written. Razor pages
/// use it as <c>@T["key"]</c> (Pages/_ViewImports.cshtml); <see cref="JsonStringLocalizer{T}"/> serves IStringLocalizer&lt;T&gt;.
/// </summary>
public class JsonStringLocalizer : IStringLocalizer
{
    private readonly ITextCatalog _catalog;
    private readonly ITextContext _context;

    public JsonStringLocalizer(ITextCatalog catalog, ITextContext context)
    {
        _catalog = catalog;
        _context = context;
    }

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
        return template is null
            ? new LocalizedString(name, name, resourceNotFound: true)
            : new LocalizedString(name, TextFormat.Apply(template, _context.Terms, arguments));
    }
}
