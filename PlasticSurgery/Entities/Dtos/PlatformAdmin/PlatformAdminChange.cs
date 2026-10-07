namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>What a platform-admin write touched, so the portal can file its audit entry under the right clinic.</summary>
public record PlatformAdminChange(Guid? ClinicId);
