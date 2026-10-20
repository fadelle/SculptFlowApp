using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Clients;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Clients;

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
        if (name.Length == 0) throw new ArgumentException("clients.errors.nameRequired");
        if (name.Length > MaxNameLength) throw new ArgumentException("clients.errors.nameTooLong").WithArgs(MaxNameLength);

        var client = await _clients.GetAsync(clientId, ct) ?? throw new ArgumentException("clients.errors.notFound");
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

    public async Task SetDefaultLanguageAsync(UserAccess actor, string? language, CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Client) throw new UnauthorizedAccessException("clients.errors.defaultLanguageOwnerOnly");
        await SetDefaultLanguageAsync(actor.ClientId, language, ct);
    }

    public async Task SetDefaultLanguageAsync(Guid clientId, string? language, CancellationToken ct = default)
    {
        var code = LanguageCode.Normalize(language) ?? throw new ArgumentException("languages.errors.required");
        var client = await _clients.GetAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        if (client.DefaultLanguage == code) return;
        client.DefaultLanguage = code;
        client.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
