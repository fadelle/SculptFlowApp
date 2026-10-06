namespace PlasticSurgery.Common.Enums;

/// <summary>Values for a source's status and a run's status — must match schema.sql's CHECKs.</summary>
public static class WebsiteScrapeStatus
{
    public const string Pending = "pending";
    public const string Crawling = "crawling";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completed_with_errors";
    public const string Failed = "failed";

    public static bool IsInProgress(string status) => status is Pending or Crawling;
}
