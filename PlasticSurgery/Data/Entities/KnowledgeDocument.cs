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

    /// <summary>The text that gets chunked and embedded. For a manual entry it's what staff typed; for an
    /// uploaded file it's the normalized text extracted from it — so re-indexing never needs the original file.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>See <see cref="KnowledgeSourceType"/>.</summary>
    public string SourceType { get; set; } = KnowledgeSourceType.Manual;

    // Upload metadata (null for manual entries). The original binary is deliberately NOT retained.
    public string? OriginalFileName { get; set; }
    public string? MimeType { get; set; }
    public long? FileSizeBytes { get; set; }

    /// <summary>The page URL a website-sourced document came from (null otherwise). The document is
    /// created/updated by the website-scraping subsystem; its text still lives in <see cref="Content"/>.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Inactive documents keep their chunks stored but are excluded from AI search.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}

/// <summary>Allowed values for KnowledgeDocument.SourceType — must match schema.sql's CHECK constraint.</summary>
public static class KnowledgeSourceType
{
    public const string Manual = "manual";
    public const string Upload = "upload";
    /// <summary>A web page imported by the website-scraping subsystem.</summary>
    public const string Website = "website";
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
