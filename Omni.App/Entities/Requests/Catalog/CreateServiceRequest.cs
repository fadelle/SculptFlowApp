namespace Omni.App.Entities.Requests.Catalog;

public record CreateServiceRequest(
    Guid ClientId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration
);
