namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for KnowledgeDocument.SourceType — must match schema.sql's CHECK constraint.</summary>
public static class KnowledgeSourceType
{
    public const string Manual = "manual";
    public const string Upload = "upload";
    /// <summary>A web page imported by the website-scraping subsystem.</summary>
    public const string Website = "website";
}
