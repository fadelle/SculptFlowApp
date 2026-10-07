namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record ProblemRow(string Kind, Guid ClinicId, string ClinicName, string Title, string? Detail, DateTimeOffset? At, string Link);
