using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record CalendarRow(Guid Id, Guid ClientId, string ClientName, string Provider, string Status, string? Account,
    string? CalendarName, bool SyncEnabled, bool IsHealthy, string? LastProblemMessage, DateTimeOffset? LastSyncedAt,
    int FailedSyncs);
