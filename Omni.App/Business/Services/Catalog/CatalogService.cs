using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Mappers.Catalog;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Catalog;
using Omni.App.Entities.Responses.Catalog;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Catalog;

namespace Omni.App.Business.Services.Catalog;

public class CatalogService : ICatalogService
{
    private const int MaxNameLength = 200;
    private const int MaxCodeLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxDurationMinutes = 480;

    private readonly IServiceRepository _services;
    private readonly IUnitOfWork _unitOfWork;

    public CatalogService(IServiceRepository services, IUnitOfWork unitOfWork)
    {
        _services = services;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ServiceResponse>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default)
    {
        var items = await _services.ListAsync(clientId, activeOnly, ct);
        return items.Select(CatalogMapper.ToResponse).ToList();
    }

    public async Task<ServiceResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var service = await _services.GetAsync(clientId, id, ct);
        return service is null ? null : CatalogMapper.ToResponse(service);
    }

    public async Task<ServiceResponse> CreateAsync(CreateServiceRequest request, CancellationToken ct = default)
    {
        var (name, code, description) = Validate(request.Name, request.Code, request.Description, request.BookingDuration);
        await EnsureNameAvailableAsync(request.ClientId, name, exceptId: null, ct);

        var service = new Service
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            Name = name,
            Code = code,
            Description = description,
            BookingDuration = request.BookingDuration,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _services.Add(service);
        await _unitOfWork.SaveChangesAsync(ct);
        return CatalogMapper.ToResponse(service);
    }

    public async Task<ServiceResponse?> UpdateAsync(Guid clientId, Guid id, UpdateServiceRequest request, CancellationToken ct = default)
    {
        var service = await _services.GetAsync(clientId, id, ct);
        if (service is null) return null;

        var (name, code, description) = Validate(request.Name, request.Code, request.Description, request.BookingDuration);
        await EnsureNameAvailableAsync(clientId, name, exceptId: id, ct);

        service.Name = name;
        service.Code = code;
        service.Description = description;
        service.BookingDuration = request.BookingDuration;
        service.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return CatalogMapper.ToResponse(service);
    }

    public async Task<ServiceResponse?> SetActiveAsync(Guid clientId, Guid id, bool isActive, CancellationToken ct = default)
    {
        var service = await _services.GetAsync(clientId, id, ct);
        if (service is null) return null;

        service.IsActive = isActive;
        service.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return CatalogMapper.ToResponse(service);
    }

    public async Task EnsureUsableAsync(Guid clientId, Guid serviceId, CancellationToken ct = default)
    {
        var service = await _services.GetReadOnlyAsync(clientId, serviceId, ct);

        if (service is null)
        {
            throw new ArgumentException("Service not found for this client.");
        }
        if (!service.IsActive)
        {
            throw new ArgumentException($"Service '{service.Name}' is inactive and can't be used for new activity.");
        }
    }

    private async Task EnsureNameAvailableAsync(Guid clientId, string name, Guid? exceptId, CancellationToken ct)
    {
        if (await _services.NameExistsAsync(clientId, name, exceptId, ct))
        {
            throw new ArgumentException($"A service named '{name}' already exists.");
        }
    }

    private static (string Name, string? Code, string? Description) Validate(
        string? name, string? code, string? description, int? bookingDuration)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (trimmedName.Length == 0) throw new ArgumentException("Name is required.");
        if (trimmedName.Length > MaxNameLength) throw new ArgumentException($"Name must be {MaxNameLength} characters or fewer.");
        if (trimmedCode is { Length: > MaxCodeLength }) throw new ArgumentException($"Code must be {MaxCodeLength} characters or fewer.");
        if (trimmedDescription is { Length: > MaxDescriptionLength })
        {
            throw new ArgumentException($"Description must be {MaxDescriptionLength} characters or fewer — put detailed information in the Knowledge Base.");
        }
        if (bookingDuration is < 1 or > MaxDurationMinutes)
        {
            throw new ArgumentException($"Booking duration must be between 1 and {MaxDurationMinutes} minutes.");
        }

        return (trimmedName, trimmedCode, trimmedDescription);
    }
}
