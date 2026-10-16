using Omni.App.Entities.Requests.Catalog;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Business.Contracts.Services.Catalog;

/// <summary>
/// A client's service catalog — structured business data (name, code, consultation duration, a short
/// description, active flag), NOT medical knowledge: detailed pricing/recovery/preparation content
/// belongs in the Knowledge Base. Services are never hard-deleted through this service, because
/// leads, appointments and orders reference them; staff deactivate instead. Everything is
/// scoped by clientId (resolved from the logged-in user by the callers).
/// </summary>
public interface ICatalogService
{
    Task<IReadOnlyList<ServiceResponse>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default);

    Task<ServiceResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default);

    /// <summary>Throws ArgumentException for invalid input (missing name, duplicate name, ...).</summary>
    Task<ServiceResponse> CreateAsync(CreateServiceRequest request, CancellationToken ct = default);

    /// <summary>Returns null if the service isn't in this client. Throws ArgumentException for invalid input.</summary>
    Task<ServiceResponse?> UpdateAsync(Guid clientId, Guid id, UpdateServiceRequest request, CancellationToken ct = default);

    /// <summary>Deactivating keeps the row and every historical reference to it; it only stops the
    /// service being offered for NEW activity (see EnsureUsableAsync) and hides it from the active
    /// lists the AI's get_services tool and the Campaign service filter read.</summary>
    Task<ServiceResponse?> SetActiveAsync(Guid clientId, Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>Guard for NEW activity that references a service (booking a consultation, creating a
    /// order, assigning service interest to a lead). Throws ArgumentException if the
    /// service isn't in this client or is inactive. Existing records that already point at a
    /// now-inactive service are untouched — this is only called when something new is being set.</summary>
    Task EnsureUsableAsync(Guid clientId, Guid serviceId, CancellationToken ct = default);
}
