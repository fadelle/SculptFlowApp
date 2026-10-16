namespace Omni.App.Common.Enums;

/// <summary>Allowed values for Contact.Qualification (how promising the contact looks, the same for every business type)
/// — must match schema.sql. Named
/// ContactQualification (not Qualification) because a class can't share its name with
/// an instance property of the type it's used to initialize — Contact.Qualification's own
/// default-value initializer needs to reference this unambiguously.</summary>
public static class ContactQualification
{
    public const string Unknown = "unknown";
    public const string Hot = "hot";
    public const string Warm = "warm";
    public const string Cold = "cold";
    public const string NeedsHuman = "needs_human";
    public const string Spam = "spam";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Unknown, Hot, Warm, Cold, NeedsHuman, Spam
    };
}
