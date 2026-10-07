using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record CalendarRow(Guid Id, Guid ClinicId, string ClinicName, string Provider, string Status, string? Account,
    string? CalendarName, bool SyncEnabled, bool IsHealthy, string? LastProblemMessage, DateTimeOffset? LastSyncedAt,
    int FailedSyncs);
