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

    private static readonly (string Value, string Label)[] Options =
    {
        (General, "General Information"),
        (Faq, "FAQ"),
        (Policy, "Policy"),
        (Doctor, "Doctor"),
        (Service, "Service Information"),
        (Pricing, "Pricing"),
        (Booking, "Booking"),
        (Payment, "Payment / Financing"),
        (Preparation, "Preparation"),
        (Recovery, "Recovery"),
    };

    public static IReadOnlyList<(string Value, string Label)> All => Options;

    public static string Label(string? value)
    {
        foreach (var (v, label) in Options)
        {
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return label;
        }
        return string.IsNullOrWhiteSpace(value) ? "—" : value!;
    }
}
