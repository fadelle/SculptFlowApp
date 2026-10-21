namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>One booking in a contact export.</summary>
public record ContactExportBooking(Guid Id, string? ServiceName, string Status, DateTimeOffset ScheduledStart, DateTimeOffset? ScheduledEnd,
    string? LocationName, string? Notes, DateTimeOffset CreatedAt);
