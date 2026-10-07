namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record KnowledgeDocRow(Guid Id, Guid ClinicId, string ClinicName, string Title, string Category, string SourceType,
    string? SourceUrl, bool IsActive, int Chunks, int ContentLength, DateTimeOffset UpdatedAt);
