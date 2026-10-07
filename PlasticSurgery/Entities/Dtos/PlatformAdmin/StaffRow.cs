using Microsoft.AspNetCore.Identity;

namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record StaffRow(string UserId, string? Email, string? FullName, Guid? MembershipId, Guid? ClinicId, string? ClinicName,
    bool MembershipActive, bool LoginLocked, bool EmailConfirmed, int AccessFailedCount, bool HasPassword, bool HasGoogleLogin,
    DateTimeOffset? JoinedAt);
