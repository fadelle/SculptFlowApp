namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record WebsiteSourceRow(Guid Id, Guid ClientId, string ClientName, string StartUrl, string CrawlMode, bool IsActive,
    string Status, int Pages, DateTimeOffset? LastScrapedAt, string? LastError);
