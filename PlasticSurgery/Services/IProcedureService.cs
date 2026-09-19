using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// A clinic's procedure catalog — structured business data (name, code, consultation duration, a short
/// description, active flag), NOT medical knowledge: detailed pricing/recovery/preparation content
/// belongs in the Knowledge Base. Procedures are never hard-deleted through this service, because
/// leads, appointments and procedure bookings reference them; staff deactivate instead. Everything is
/// scoped by clinicId (resolved from the logged-in user by the callers).
/// </summary>
public interface IProcedureService
{
    Task<IReadOnlyList<ProcedureResponse>> ListAsync(Guid clinicId, bool activeOnly, CancellationToken ct = default);

    Task<ProcedureResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    /// <summary>Throws ArgumentException for invalid input (missing name, duplicate name, ...).</summary>
    Task<ProcedureResponse> CreateAsync(CreateProcedureRequest request, CancellationToken ct = default);

    /// <summary>Returns null if the procedure isn't in this clinic. Throws ArgumentException for invalid input.</summary>
    Task<ProcedureResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateProcedureRequest request, CancellationToken ct = default);

    /// <summary>Deactivating keeps the row and every historical reference to it; it only stops the
    /// procedure being offered for NEW activity (see EnsureUsableAsync) and hides it from the active
    /// lists the AI's get_procedures tool and the Campaign procedure filter read.</summary>
    Task<ProcedureResponse?> SetActiveAsync(Guid clinicId, Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>Guard for NEW activity that references a procedure (booking a consultation, creating a
    /// procedure booking, assigning procedure interest to a lead). Throws ArgumentException if the
    /// procedure isn't in this clinic or is inactive. Existing records that already point at a
    /// now-inactive procedure are untouched — this is only called when something new is being set.</summary>
    Task EnsureUsableAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default);
}
