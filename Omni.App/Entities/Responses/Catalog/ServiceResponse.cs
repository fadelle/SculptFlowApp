namespace Omni.App.Entities.Responses.Catalog;

public record ServiceResponse(
    Guid Id,
    Guid ClientId,
    string Name,
    string? Code,
    string? Description,
    int? ConsultationDuration,
    bool IsActive
);
