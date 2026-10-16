namespace Omni.App.Entities.Dtos.Clients;

public record StaffMemberRow(string UserId, string? Email, bool IsActive, DateTimeOffset CreatedAt, string? FullName);
