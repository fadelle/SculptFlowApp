namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ProcedureRow(Guid Id, Guid ClientId, string ClientName, string Name, string? Code, int? ConsultationDuration,
    bool IsActive, int Leads, DateTimeOffset UpdatedAt);
