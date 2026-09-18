using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IProcedureService
{
    Task<IReadOnlyList<ProcedureResponse>> ListAsync(Guid clinicId, bool activeOnly, CancellationToken ct = default);
    Task<ProcedureResponse> CreateAsync(CreateProcedureRequest request, CancellationToken ct = default);
}
