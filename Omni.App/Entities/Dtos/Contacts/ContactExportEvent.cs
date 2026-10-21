namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>One activity entry about the contact in an export (what happened and when; no internal metadata).</summary>
public record ContactExportEvent(string EventType, string? Source, DateTimeOffset CreatedAt);
