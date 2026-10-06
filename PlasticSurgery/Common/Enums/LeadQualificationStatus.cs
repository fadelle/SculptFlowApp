namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for Lead.QualificationStatus — must match schema.sql. Named
/// LeadQualificationStatus (not QualificationStatus) because a class can't share its name with
/// an instance property of the type it's used to initialize — Lead.QualificationStatus's own
/// default-value initializer needs to reference this unambiguously.</summary>
public static class LeadQualificationStatus
{
    public const string Unknown = "unknown";
    public const string Hot = "hot";
    public const string Warm = "warm";
    public const string Cold = "cold";
    public const string NeedsHuman = "needs_human";
    public const string MedicalQuestion = "medical_question";
    public const string Spam = "spam";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Unknown, Hot, Warm, Cold, NeedsHuman, MedicalQuestion, Spam
    };
}
