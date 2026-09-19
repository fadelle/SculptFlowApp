namespace PlasticSurgery.Data.Entities;

/// <summary>
/// One piece of clinic-approved knowledge staff typed into the Knowledge Base (an FAQ, a policy,
/// doctor info, pricing, ...). The AI agent never reads this table directly — content is split into
/// KnowledgeChunk rows (with embeddings) and searched semantically via IKnowledgeSearchService.
/// Everything here is scoped by ClinicId; knowledge from one clinic is never visible to another.
/// </summary>
public class KnowledgeDocument
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Free string (see <see cref="KnowledgeCategory"/> for the values the UI offers) —
    /// deliberately no DB CHECK, like campaigns.campaign_type, so new categories need no migration.</summary>
    public string Category { get; set; } = KnowledgeCategory.General;

    public string Content { get; set; } = string.Empty;

    /// <summary>Inactive documents keep their chunks stored but are excluded from AI search.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}

public static class KnowledgeCategory
{
    public const string General = "general";
    public const string Faq = "faq";
    public const string Policy = "policy";
    public const string Doctor = "doctor";
    public const string Procedure = "procedure";
    public const string Pricing = "pricing";
    public const string Consultation = "consultation";
    public const string Payment = "payment";
    public const string Preparation = "preparation";
    public const string Recovery = "recovery";

    private static readonly (string Value, string Label)[] Options =
    {
        (General, "General Information"),
        (Faq, "FAQ"),
        (Policy, "Policy"),
        (Doctor, "Doctor"),
        (Procedure, "Procedure Information"),
        (Pricing, "Pricing"),
        (Consultation, "Consultation"),
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
