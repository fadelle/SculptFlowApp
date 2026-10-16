using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Clients;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Clients;

public class ClientProfileService : IClientProfileService
{
    private const int MaxNameLength = 200;

    private readonly IClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;

    public ClientProfileService(IClientRepository clients, IUnitOfWork unitOfWork)
    {
        _clients = clients;
        _unitOfWork = unitOfWork;
    }

    public async Task UpdateDetailsAsync(Guid clientId, UpdateClientDetailsRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("Client name is required.");
        if (name.Length > MaxNameLength) throw new ArgumentException($"Client name must be {MaxNameLength} characters or fewer.");

        var client = await _clients.GetAsync(clientId, ct) ?? throw new ArgumentException("Client not found.");
        client.Name = name;
        client.Phone = request.Phone;
        client.Email = request.Email;
        client.Website = request.Website;
        client.Address = request.Address;
        client.OperatingHours = request.OperatingHours;
        client.BookingInfo = request.BookingInfo;
        client.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
