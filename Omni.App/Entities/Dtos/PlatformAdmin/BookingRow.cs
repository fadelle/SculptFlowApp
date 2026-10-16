namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record BookingRow(Guid Id, Guid ClientId, string ClientName, string ClientTimezone, Guid LeadId, string LeadName,
    string? ServiceName, string Status, DateTimeOffset ScheduledStart, DateTimeOffset? ScheduledEnd, string? Notes,
    DateTimeOffset CreatedAt);
