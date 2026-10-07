using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record TikTokRow(Guid Id, Guid ClinicId, string ClinicName, string Status, string? DisplayName, bool IsHealthy,
    string? LastProblemMessage, DateTimeOffset? TokenExpiresAt, DateTimeOffset UpdatedAt);
