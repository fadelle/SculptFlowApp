using Omni.App.Entities.Requests.Clients;

namespace Omni.App.Business.Contracts.Services.Clients;

/// <summary>The client's own details (name, contact info, opening-hours text) edited on Business Info.</summary>
public interface IClientProfileService
{
    /// <summary>Throws ArgumentException (with a message for the form) when the name is missing or too long.</summary>
    Task UpdateDetailsAsync(Guid clientId, UpdateClientDetailsRequest request, CancellationToken ct = default);
}
