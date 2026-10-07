namespace PlasticSurgery.Common.Enums;

public static class WhatsAppTemplateCategory
{
    public const string Marketing = "marketing";
    public const string Utility = "utility";
    public const string Authentication = "authentication";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Marketing, Utility, Authentication };
}
