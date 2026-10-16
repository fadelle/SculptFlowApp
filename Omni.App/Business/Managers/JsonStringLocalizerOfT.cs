using Microsoft.Extensions.Localization;

namespace Omni.App.Business.Managers;

/// <summary>IStringLocalizer&lt;T&gt; over the same strings files as <see cref="JsonStringLocalizer"/> (one shared set, not
/// one per class).</summary>
public class JsonStringLocalizer<T> : IStringLocalizer<T>
{
    private readonly IStringLocalizer _inner;

    public JsonStringLocalizer(IStringLocalizer inner) => _inner = inner;

    public LocalizedString this[string name] => _inner[name];

    public LocalizedString this[string name, params object[] arguments] => _inner[name, arguments];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _inner.GetAllStrings(includeParentCultures);
}
