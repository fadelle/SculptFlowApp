namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>What a platform-admin write touched, so the portal can file its audit entry under the right client.</summary>
public record PlatformAdminChange(Guid? ClientId);
