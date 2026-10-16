using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record CampaignRow(Guid Id, Guid ClientId, string ClientName, string Name, string CampaignType, string Channel,
    string Status, string? TemplateName, int Recipients, int Sent, int Delivered, int Read, int Replied, int Booked, int Failed,
    int Skipped, DateTimeOffset? ScheduledAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt);
