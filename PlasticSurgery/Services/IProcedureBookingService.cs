using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IProcedureBookingService
{
    Task<ProcedureBookingResponse> CreateAsync(CreateProcedureBookingRequest request, CancellationToken ct = default);

    Task<ProcedureBookingResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateProcedureBookingRequest request, CancellationToken ct = default);

    Task<(IReadOnlyList<ProcedureBookingResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, int skip, int take, CancellationToken ct = default);
}
