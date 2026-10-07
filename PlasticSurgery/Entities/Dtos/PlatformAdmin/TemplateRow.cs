namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record TemplateRow(Guid Id, Guid ClinicId, string ClinicName, string Name, string? Provider, string Category, string Language, string Status,
    string? QualityRating, string? RejectionReason, string Body, DateTimeOffset UpdatedAt);
