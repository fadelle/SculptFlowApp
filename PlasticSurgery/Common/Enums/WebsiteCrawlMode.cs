namespace PlasticSurgery.Common.Enums;

public static class WebsiteCrawlMode
{
    public const string SinglePage = "single_page";
    public const string CrawlSite = "crawl_site";
    public static bool IsValid(string? v) => v is SinglePage or CrawlSite;
}
