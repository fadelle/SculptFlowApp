using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Texts that change with a count: <c>@T.Plural("services.count", n)</c> reads "services.count.{category}" for the
/// localizer's language (<see cref="PluralRules"/>), falling back to "services.count.other". With no other arguments the
/// count is {0}. For a text with an HTML argument: <c>@T.Html(T.PluralKey("contacts.count", n), html)</c>. The browser twin is
/// <c>omniText.plural('services.count', n)</c>. Guards/UiTextKeysTests reads the key family from these calls.
/// </summary>
public static class TextPlural
{
    public static LocalizedString Plural(this IStringLocalizer text, string key, long count, params object?[] args) =>
        text[text.PluralKey(key, count), args.Length == 0 ? [count] : (object[])args!];

    /// <summary>The key of the form a count needs: "key.{category}" when the strings have it, else "key.other".</summary>
    public static string PluralKey(this IStringLocalizer text, string key, long count)
    {
        var language = (text as ITextLanguage)?.Language ?? AppLanguage.English;
        var exact = $"{key}.{PluralRules.Category(language, count)}";
        return text[exact].ResourceNotFound ? $"{key}.{PluralRules.Other}" : exact;
    }
}
