namespace PlasticSurgery.Entities.Dtos.Clinics;

public record StaffMemberRow(string UserId, string? Email, bool IsActive, DateTimeOffset CreatedAt, string? FullName);
