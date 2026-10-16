using Omni.App.Entities.Requests.Procedures;
using Omni.App.Entities.Responses.Procedures;

namespace Omni.App.Business.Contracts.Services.Procedures;

public interface IProcedureBookingService
{
    Task<ProcedureBookingResponse> CreateAsync(CreateProcedureBookingRequest request, CancellationToken ct = default);

    Task<ProcedureBookingResponse?> UpdateAsync(Guid clientId, Guid id, UpdateProcedureBookingRequest request, CancellationToken ct = default);

    Task<(IReadOnlyList<ProcedureBookingResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, int skip, int take, CancellationToken ct = default);
}
