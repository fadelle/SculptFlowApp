namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record WebsiteSourceRow(Guid Id, Guid ClinicId, string ClinicName, string StartUrl, string CrawlMode, bool IsActive,
    string Status, int Pages, DateTimeOffset? LastScrapedAt, string? LastError);
