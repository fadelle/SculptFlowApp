using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record TikTokRow(Guid Id, Guid ClientId, string ClientName, string Status, string? DisplayName, bool IsHealthy,
    string? LastProblemMessage, DateTimeOffset? TokenExpiresAt, DateTimeOffset UpdatedAt);
