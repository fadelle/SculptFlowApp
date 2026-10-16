using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ChannelRow(Guid Id, Guid ClientId, string ClientName, string Channel, string Status,
    string? DisplayName, string? SenderId, string? HealthLevel, bool IsHealthy, string? LastProblemMessage, string? LastError,
    string? WebhookStatus, DateTimeOffset? LastWebhookAt, DateTimeOffset? LastVerifiedAt, DateTimeOffset UpdatedAt);
