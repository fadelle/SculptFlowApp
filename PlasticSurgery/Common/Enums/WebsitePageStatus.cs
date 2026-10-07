namespace PlasticSurgery.Common.Enums;

/// <summary>Values for KnowledgeWebsitePage.Status — must match schema.sql's CHECK.</summary>
public static class WebsitePageStatus
{
    public const string Discovered = "discovered";
    public const string Processing = "processing";
    public const string Indexed = "indexed";
    public const string Unchanged = "unchanged";
    public const string Skipped = "skipped";
    public const string Duplicate = "duplicate";
    public const string Failed = "failed";
    public const string Removed = "removed";
}
