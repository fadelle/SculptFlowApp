using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Requests.Clients;

namespace Omni.App.Business.Contracts.Services.Clients;

/// <summary>The client's own details (name, contact info, opening-hours text) edited on Business Info.</summary>
public interface IClientProfileService
{
    /// <summary>Throws ArgumentException (with a message for the form) when the name is missing or too long.</summary>
    Task UpdateDetailsAsync(Guid clientId, UpdateClientDetailsRequest request, CancellationToken ct = default);

    /// <summary>The language the client's dashboard opens in for users who haven't picked their own (en, ar, fr). Only the
    /// account owner (role client) may change it (UnauthorizedAccessException otherwise); ArgumentException for an
    /// unknown language.</summary>
    Task SetDefaultLanguageAsync(UserAccess actor, string? language, CancellationToken ct = default);

    /// <summary>The same for the platform admin: any client by id. KeyNotFoundException for an unknown client.</summary>
    Task SetDefaultLanguageAsync(Guid clientId, string? language, CancellationToken ct = default);
}
