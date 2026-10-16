namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ServiceRow(Guid Id, Guid ClientId, string ClientName, string Name, string? Code, int? BookingDuration,
    bool IsActive, int Leads, DateTimeOffset UpdatedAt);
