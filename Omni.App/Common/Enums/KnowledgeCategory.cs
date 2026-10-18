using Microsoft.Extensions.Localization;
using Omni.App.Entities.Models;

namespace Omni.App.Common.Enums;

public static class KnowledgeCategory
{
    public const string General = "general";
    public const string Faq = "faq";
    public const string Policy = "policy";
    public const string Doctor = "doctor";
    public const string Service = "service";
    public const string Pricing = "pricing";
    public const string Booking = "booking";
    public const string Payment = "payment";
    public const string Preparation = "preparation";
    public const string Recovery = "recovery";

    /// <summary>Each category's label is the UI string <c>knowledge.category.{value}</c> (Resources/i18n).</summary>
    public const string LabelKeyPrefix = "knowledge.category.";

    public static IReadOnlyList<string> All { get; } =
        [General, Faq, Policy, Doctor, Service, Pricing, Booking, Payment, Preparation, Recovery];

    /// <summary>The category's label in the request's language; a custom category shows as stored.</summary>
    public static string CategoryLabel(this IStringLocalizer text, string? value)
    {
        var known = All.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return text[LabelKeyPrefix + known].Value;
        return string.IsNullOrWhiteSpace(value) ? "—" : value!;
    }
}
